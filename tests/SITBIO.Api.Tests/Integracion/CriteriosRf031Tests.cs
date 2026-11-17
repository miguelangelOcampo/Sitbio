using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Fotografía del equipo (RF-031; CA-HU-015-03 y CA-HU-015-04, parte de la API; CP-035) contra la API,
/// PostgreSQL, MongoDB y el almacenamiento reales: el Ingeniero Biomédico (AC-02) sube la miniatura de
/// 128 px y la vista de 512 px (el original no llega ni se guarda), el espacio cuenta en el plan
/// (RN-009), la hoja de vida indica que hay foto y la entrega por tamaño a los roles que la consultan;
/// otra institución no la ve ni la toca (SEC-003). Cada prueba usa su propia institución.
/// </summary>
public class CriteriosRf031Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/equipos";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);
    private static readonly byte[] Miniatura = Webp(128, 128, relleno: 300);
    private static readonly byte[] Vista = Webp(512, 384, relleno: 2000);

    private sealed record Institucion(Guid Tenant, Guid IngenieroId, HttpClient Ingeniero, Guid Equipo);

    private async Task<Institucion> InstitucionConEquipo(string plan = "Clínica", int almacenamientoGb = 25)
    {
        var tenant = await api.CrearTenantAsync(plan, 7, 1000, almacenamientoGb);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Quirófanos");
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Máquina de anestesia", ClasesRiesgo.IIb, true, 12);
        var correo = api.Correo($"f31-ingeniero-{tenant:N}");
        var ingenieroId = await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        var ingeniero = await api.IniciarSesionAsync(correo);

        var placa = $"F31-{tenant.ToString("N")[..6]}";
        var respuesta = await ingeniero.PostAsJsonAsync(Ruta, new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = "Dräger",
            ["modelo"] = "Fabius plus",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2019DM-0019876",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var equipo = Guid.Parse((await Cuerpo(respuesta)).GetProperty("id").GetString()!);
        return new Institucion(tenant, ingenieroId, ingeniero, equipo);
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"f31-{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static Task<HttpResponseMessage> Subir(HttpClient cliente, Guid equipo, byte[]? miniatura = null, byte[]? vista = null, bool aviso = true)
    {
        var formulario = new MultipartFormDataContent();
        formulario.Add(new StringContent(aviso ? "true" : "false"), "avisoAceptado");
        Parte(formulario, "miniatura", miniatura ?? Miniatura);
        Parte(formulario, "vista", vista ?? Vista);
        return cliente.PutAsync($"{Ruta}/{equipo}/foto", formulario);
    }

    private static void Parte(MultipartFormDataContent formulario, string nombre, byte[] contenido)
    {
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
        formulario.Add(archivo, nombre, $"{nombre}.webp");
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<bool> TieneFoto(HttpClient cliente, Guid equipo) =>
        (await Cuerpo(await cliente.GetAsync($"{Ruta}/{equipo}"))).GetProperty("tieneFoto").GetBoolean();

    private string ArchivosDe(Institucion i) => Path.Combine(api.RaizAdjuntos, i.Tenant.ToString(), i.Equipo.ToString());

    /// <summary>Cabecera WebP extendida (VP8X) con el tamaño pedido; el resto es relleno.</summary>
    private static byte[] Webp(int ancho, int alto, int relleno)
    {
        var datos = new byte[30 + relleno];
        "RIFF"u8.CopyTo(datos);
        BinaryPrimitives.WriteInt32LittleEndian(datos.AsSpan(4), datos.Length - 8);
        "WEBPVP8X"u8.CopyTo(datos.AsSpan(8));
        datos[16] = 10;
        void Lado(int desde, int valor)
        {
            datos[desde] = (byte)(valor - 1);
            datos[desde + 1] = (byte)((valor - 1) >> 8);
            datos[desde + 2] = (byte)((valor - 1) >> 16);
        }

        Lado(24, ancho);
        Lado(27, alto);
        return datos;
    }

    // ---------- CA-HU-015-03 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_015_03_SeGuardanSoloLas2VersionesReducidas_CuentanEnElPlanYQuedaEnLaBitacora()
    {
        var i = await InstitucionConEquipo();
        Assert.False(await TieneFoto(i.Ingeniero, i.Equipo));

        var respuesta = await Subir(i.Ingeniero, i.Equipo);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var foto = await Cuerpo(respuesta);
        var id = Guid.Parse(foto.GetProperty("documentoId").GetString()!);
        Assert.Equal("image/webp", foto.GetProperty("tipoMime").GetString());
        Assert.Equal(Miniatura.Length + Vista.Length, foto.GetProperty("tamanoBytes").GetInt64());

        // Un documento «Fotografía» con el tamaño de las dos versiones (cuenta en el almacenamiento del plan).
        var guardado = Assert.Single(await api.LeerDocumentosAsync(i.Equipo));
        Assert.Equal((id, TiposDocumento.Fotografia, (long)(Miniatura.Length + Vista.Length), true), (guardado.Id, guardado.TipoDocumento, guardado.TamanoBytes, guardado.EstadoActivo));
        Assert.Equal($"{i.Tenant}/{i.Equipo}/{id:N}.webp", guardado.RutaAlmacenamiento);

        // En disco solo están la vista y la miniatura; nada más.
        Assert.Equal(
            new[] { $"{id:N}-128.webp", $"{id:N}.webp" },
            Directory.EnumerateFiles(ArchivosDe(i)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(Vista, await File.ReadAllBytesAsync(Path.Combine(ArchivosDe(i), $"{id:N}.webp")));
        Assert.Equal(Miniatura, await File.ReadAllBytesAsync(Path.Combine(ArchivosDe(i), $"{id:N}-128.webp")));

        // La foto no aparece en la pestaña Documentos.
        Assert.Empty((await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{i.Equipo}/documentos"))).EnumerateArray());

        // Bitácora CREAR con la constancia del aviso de datos personales (Ley 1581 de 2012).
        var evento = (await api.LeerBitacoraAsync(i.Tenant)).First(d => d["accion"] == "CREAR" && d["entidad"] == GestionFotoEquipo.Entidad);
        Assert.Equal(id.ToString(), evento["entidad_id"].AsString);
        Assert.Equal(TiposDocumento.Fotografia, evento["detalle"]["tipo"].AsString);
        Assert.Equal("aceptado", evento["detalle"]["aviso_datos_personales"].AsString);
    }

    [FactIntegracion]
    public async Task CA_HU_015_03_ImagenSinReducirOSinAviso_SeRechazaCon400YNoSeGuarda()
    {
        var i = await InstitucionConEquipo();

        var original = await Subir(i.Ingeniero, i.Equipo, vista: Webp(4032, 3024, 100));
        var miniaturaMal = await Subir(i.Ingeniero, i.Equipo, miniatura: Webp(256, 256, 100));
        var sinAviso = await Subir(i.Ingeniero, i.Equipo, aviso: false);
        var noImagen = await Subir(i.Ingeniero, i.Equipo, vista: "%PDF-1.7 no es una imagen"u8.ToArray());

        Assert.All(new[] { original, miniaturaMal, sinAviso, noImagen }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.True((await Cuerpo(original)).GetProperty("errors").TryGetProperty("vista", out _));
        Assert.True((await Cuerpo(miniaturaMal)).GetProperty("errors").TryGetProperty("miniatura", out _));
        Assert.Equal(GestionFotoEquipo.AvisoDatosPersonales, (await Cuerpo(sinAviso)).GetProperty("errors").GetProperty("aviso")[0].GetString());
        Assert.Equal(GestionFotoEquipo.FormatoNoPermitido, (await Cuerpo(noImagen)).GetProperty("errors").GetProperty("vista")[0].GetString());

        Assert.Empty(await api.LeerDocumentosAsync(i.Equipo));
        Assert.False(Directory.Exists(ArchivosDe(i)) && Directory.EnumerateFiles(ArchivosDe(i)).Any());
        Assert.False(await TieneFoto(i.Ingeniero, i.Equipo));
    }

    [FactIntegracion]
    public async Task CA_HU_015_03_AlmacenamientoDelPlanLleno_RechazaConMsg019()
    {
        var i = await InstitucionConEquipo("Consultorio", almacenamientoGb: 5);
        await api.OcuparAlmacenamientoAsync(i.Tenant, i.Equipo, i.IngenieroId, (5L * 1024 * 1024 * 1024) - Vista.Length);

        var rechazo = await Subir(i.Ingeniero, i.Equipo);

        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        var cuerpo = await Cuerpo(rechazo);
        Assert.Equal(GestionDocumentos.CodigoLimitePlan, cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("máximo de almacenamiento del plan Consultorio (5 GB)", cuerpo.GetProperty("detail").GetString());
        Assert.False(await TieneFoto(i.Ingeniero, i.Equipo));
    }

    // ---------- CA-HU-015-04 (lo que entrega la API) ----------

    [FactIntegracion]
    public async Task CA_HU_015_04_LaHojaDeVidaIndicaQueHayFoto_YLaEntregaPorTamanoALosRolesQueConsultan()
    {
        var i = await InstitucionConEquipo();
        Assert.Equal(HttpStatusCode.OK, (await Subir(i.Ingeniero, i.Equipo)).StatusCode);

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.IngenieroBiomedico, CodigosRol.TecnicoMantenimiento, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var cliente = await SesionDe(i.Tenant, rol);
            Assert.True(await TieneFoto(cliente, i.Equipo));

            var miniatura = await cliente.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=128");
            var vista = await cliente.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=512");
            var porDefecto = await cliente.GetAsync($"{Ruta}/{i.Equipo}/foto");

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (miniatura.StatusCode, vista.StatusCode, porDefecto.StatusCode));
            Assert.Equal("image/webp", miniatura.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Miniatura, await miniatura.Content.ReadAsByteArrayAsync());
            Assert.Equal(Vista, await vista.Content.ReadAsByteArrayAsync());
            Assert.Equal(Vista, await porDefecto.Content.ReadAsByteArrayAsync());
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await i.Ingeniero.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=1024")).StatusCode);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CambiarYQuitar_LaAnteriorSeDesactivaYLaHojaDeVidaVuelveAlIcono()
    {
        var i = await InstitucionConEquipo();
        var primera = Guid.Parse((await Cuerpo(await Subir(i.Ingeniero, i.Equipo))).GetProperty("documentoId").GetString()!);
        var nuevaVista = Webp(384, 512, relleno: 900);

        var cambio = await Subir(i.Ingeniero, i.Equipo, vista: nuevaVista);

        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var segunda = Guid.Parse((await Cuerpo(cambio)).GetProperty("documentoId").GetString()!);
        Assert.NotEqual(primera, segunda);
        Assert.Equal(nuevaVista, await (await i.Ingeniero.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=512")).Content.ReadAsByteArrayAsync());
        var documentos = await api.LeerDocumentosAsync(i.Equipo);
        Assert.False(documentos.Single(d => d.Id == primera).EstadoActivo);
        Assert.True(documentos.Single(d => d.Id == segunda).EstadoActivo);

        var quitar = await i.Ingeniero.DeleteAsync($"{Ruta}/{i.Equipo}/foto");

        Assert.Equal(HttpStatusCode.NoContent, quitar.StatusCode);
        Assert.False(await TieneFoto(i.Ingeniero, i.Equipo));
        Assert.Equal(HttpStatusCode.NotFound, (await i.Ingeniero.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=128")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await i.Ingeniero.DeleteAsync($"{Ruta}/{i.Equipo}/foto")).StatusCode);
        Assert.All(await api.LeerDocumentosAsync(i.Equipo), d => Assert.False(d.EstadoActivo));

        var evento = (await api.LeerBitacoraAsync(i.Tenant)).First(d => d["accion"] == "ELIMINAR" && d["entidad"] == GestionFotoEquipo.Entidad);
        Assert.Equal(segunda.ToString(), evento["entidad_id"].AsString);
        Assert.DoesNotContain("Fotografía del equipo.webp", evento.ToJson());
    }

    // ---------- RBAC (matriz de permisos) ----------

    [FactIntegracion]
    public async Task SoloElIngenieroSubeOQuita_LaDirectivaNoConsulta()
    {
        var i = await InstitucionConEquipo();
        Assert.Equal(HttpStatusCode.OK, (await Subir(i.Ingeniero, i.Equipo)).StatusCode);

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var cliente = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await Subir(cliente, i.Equipo)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.DeleteAsync($"{Ruta}/{i.Equipo}/foto")).StatusCode);
        }

        var directiva = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);
        Assert.Equal(HttpStatusCode.Forbidden, (await directiva.GetAsync($"{Ruta}/{i.Equipo}/foto?tam=128")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Ruta}/{i.Equipo}/foto?tam=128")).StatusCode);
        Assert.True((await api.LeerDocumentosAsync(i.Equipo)).Single().EstadoActivo);
    }

    // ---------- Aislamiento entre tenants (SEC-003) ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoVeNiCambiaNiQuitaLaFoto()
    {
        var a = await InstitucionConEquipo();
        var b = await InstitucionConEquipo();
        Assert.Equal(HttpStatusCode.OK, (await Subir(a.Ingeniero, a.Equipo)).StatusCode);
        var consultaB = await SesionDe(b.Tenant, CodigosRol.AdministradorSistema);

        Assert.Equal(HttpStatusCode.NotFound, (await consultaB.GetAsync($"{Ruta}/{a.Equipo}/foto?tam=512")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Subir(b.Ingeniero, a.Equipo)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.DeleteAsync($"{Ruta}/{a.Equipo}/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{b.Equipo}/foto")).StatusCode);

        Assert.True((await api.LeerDocumentosAsync(a.Equipo)).Single().EstadoActivo);
        Assert.True(await TieneFoto(a.Ingeniero, a.Equipo));
        Assert.False(Directory.Exists(Path.Combine(api.RaizAdjuntos, b.Tenant.ToString(), a.Equipo.ToString())));
    }
}
