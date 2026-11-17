using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-015 (DOC-04; CP-019) contra la API, PostgreSQL, MongoDB y el
/// almacenamiento de archivos reales: el Ingeniero Biomédico (AC-02) adjunta PDF, JPG o PNG
/// (CA-HU-015-01), cualquier otro formato se rechaza con 400 (CA-HU-015-02) y otra institución no
/// ve ni toca los documentos (SEC-003). Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu015Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/equipos";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private sealed record Institucion(Guid Tenant, Guid IngenieroId, HttpClient Ingeniero, Guid Equipo);

    private async Task<Institucion> InstitucionConEquipo(string plan = "Clínica", int almacenamientoGb = 25)
    {
        var tenant = await api.CrearTenantAsync(plan, 7, 1000, almacenamientoGb);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.IIb, true, 12);
        var correo = api.Correo($"h15-ingeniero-{tenant:N}");
        var ingenieroId = await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        var ingeniero = await api.IniciarSesionAsync(correo);

        var placa = $"H15-{tenant.ToString("N")[..6]}";
        var respuesta = await ingeniero.PostAsJsonAsync(Ruta, new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = "Mindray",
            ["modelo"] = "uMEC 12",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2020DM-0021345",
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
        var correo = api.Correo($"h15-{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static Task<HttpResponseMessage> Adjuntar(HttpClient cliente, Guid equipo, string tipo, string nombre, byte[] contenido)
    {
        var formulario = new MultipartFormDataContent();
        formulario.Add(new StringContent(tipo), "tipo");
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formulario.Add(archivo, "archivo", nombre);
        return cliente.PostAsync($"{Ruta}/{equipo}/documentos", formulario);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<List<JsonElement>> Listar(HttpClient cliente, Guid equipo)
    {
        var respuesta = await cliente.GetAsync($"{Ruta}/{equipo}/documentos");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await Cuerpo(respuesta)).EnumerateArray().ToList();
    }

    private string ArchivosDe(Institucion i) => Path.Combine(api.RaizAdjuntos, i.Tenant.ToString(), i.Equipo.ToString());

    // ---------- CA-HU-015-01 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_015_01_ArchivoPermitido_QuedaVinculadoYVisibleEnLaHojaDeVida()
    {
        var i = await InstitucionConEquipo();
        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);

        var respuesta = await Adjuntar(i.Ingeniero, i.Equipo, "Manual de usuario", "Manual uMEC 12.pdf", Pdf);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await Cuerpo(respuesta);
        var id = Guid.Parse(creado.GetProperty("id").GetString()!);
        Assert.Equal("Manual uMEC 12.pdf", creado.GetProperty("nombre").GetString());
        Assert.Equal("Manual de usuario", creado.GetProperty("tipo").GetString());
        Assert.Equal("application/pdf", creado.GetProperty("tipoMime").GetString());
        Assert.Equal(Pdf.Length, creado.GetProperty("tamanoBytes").GetInt64());
        Assert.Equal("Prueba HU-001", creado.GetProperty("subidoPor").GetString());

        // En PostgreSQL: vinculado al equipo, con quién lo subió; en disco, en <tenant>/<equipo>/ con el id como nombre.
        var guardado = Assert.Single(await api.LeerDocumentosAsync(i.Equipo));
        Assert.Equal((i.Tenant, i.IngenieroId, true), (guardado.TenantId, guardado.SubidoPorId, guardado.EstadoActivo));
        Assert.InRange(guardado.FechaCarga, antes, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal($"{i.Tenant}/{i.Equipo}/{id:N}.pdf", guardado.RutaAlmacenamiento);
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(Path.Combine(ArchivosDe(i), $"{id:N}.pdf")));

        // Visible en la hoja de vida para los roles que la consultan (AC-01 a AC-05 y AC-07).
        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var cliente = await SesionDe(i.Tenant, rol);
            var fila = Assert.Single(await Listar(cliente, i.Equipo));
            Assert.Equal(id.ToString(), fila.GetProperty("id").GetString());

            var descarga = await cliente.GetAsync($"{Ruta}/{i.Equipo}/documentos/{id}/archivo");
            Assert.Equal(HttpStatusCode.OK, descarga.StatusCode);
            Assert.Equal("application/pdf", descarga.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Pdf, await descarga.Content.ReadAsByteArrayAsync());
            Assert.Equal("Manual uMEC 12.pdf", descarga.Content.Headers.ContentDisposition?.FileNameStar ?? descarga.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }

        // Bitácora CREAR sin el nombre del archivo (puede llevar datos personales).
        var evento = (await api.LeerBitacoraAsync(i.Tenant)).First(d => d["accion"] == "CREAR" && d["entidad"] == GestionDocumentos.Entidad);
        Assert.Equal(id.ToString(), evento["entidad_id"].AsString);
        Assert.Equal("Manual de usuario", evento["detalle"]["tipo"].AsString);
        Assert.DoesNotContain("uMEC", evento.ToJson());
    }

    [FactIntegracion]
    public async Task CA_HU_015_01_JpgYPng_SeAceptanYSeListanDelMasRecienteAlMasAntiguo()
    {
        var i = await InstitucionConEquipo();

        var jpg = await Adjuntar(i.Ingeniero, i.Equipo, "Fotografía", "placa.JPEG", Jpg);
        var png = await Adjuntar(i.Ingeniero, i.Equipo, "Ficha técnica", "ficha.png", Png);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (jpg.StatusCode, png.StatusCode));
        Assert.Equal(("image/jpeg", "image/png"), ((await Cuerpo(jpg)).GetProperty("tipoMime").GetString(), (await Cuerpo(png)).GetProperty("tipoMime").GetString()));
        Assert.Equal(["ficha.png", "placa.JPEG"], (await Listar(i.Ingeniero, i.Equipo)).Select(f => f.GetProperty("nombre").GetString()));
    }

    // ---------- CA-HU-015-02 ----------

    [FactIntegracion]
    public async Task CA_HU_015_02_FormatoNoPermitido_SeRechazaCon400YNoSeGuarda()
    {
        var i = await InstitucionConEquipo();
        var casos = new (string Nombre, byte[] Contenido)[]
        {
            ("instalador.exe", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]),
            ("notas.txt", Encoding.UTF8.GetBytes("Calibrado el lunes")),
            ("informe.docx", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]),
            ("foto.webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")),
            // Extensión permitida, contenido de otro tipo: también se rechaza.
            ("renombrado.pdf", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]),
        };

        foreach (var (nombre, contenido) in casos)
        {
            var respuesta = await Adjuntar(i.Ingeniero, i.Equipo, "Otro", nombre, contenido);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var errores = (await Cuerpo(respuesta)).GetProperty("errors");
            Assert.Equal(GestionDocumentos.FormatoNoPermitido, errores.GetProperty("archivo")[0].GetString());
        }

        Assert.Empty(await api.LeerDocumentosAsync(i.Equipo));
        Assert.False(Directory.Exists(ArchivosDe(i)) && Directory.EnumerateFiles(ArchivosDe(i)).Any());
    }

    [FactIntegracion]
    public async Task CA_HU_015_02_ArchivoMayorAlMaximoOSinTipo_SeRechazaCon400()
    {
        var i = await InstitucionConEquipo();
        var grande = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(grande, 0);

        var muyGrande = await Adjuntar(i.Ingeniero, i.Equipo, "Manual de usuario", "manual.pdf", grande);
        var sinTipo = await Adjuntar(i.Ingeniero, i.Equipo, "Contrato", "contrato.pdf", Pdf);

        Assert.Equal(HttpStatusCode.BadRequest, muyGrande.StatusCode);
        Assert.Equal("El archivo supera 10 MB.", (await Cuerpo(muyGrande)).GetProperty("errors").GetProperty("archivo")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinTipo.StatusCode);
        Assert.True((await Cuerpo(sinTipo)).GetProperty("errors").TryGetProperty("tipo", out _));
        Assert.Empty(await api.LeerDocumentosAsync(i.Equipo));
    }

    [FactIntegracion]
    public async Task MismoNombreEnElEquipo_ResponderConflicto()
    {
        var i = await InstitucionConEquipo();
        await Adjuntar(i.Ingeniero, i.Equipo, "Manual de usuario", "manual.pdf", Pdf);

        var repetido = await Adjuntar(i.Ingeniero, i.Equipo, "Otro", "MANUAL.pdf", Pdf);

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(GestionDocumentos.CodigoNombreRepetido, (await Cuerpo(repetido)).GetProperty("codigo").GetString());
    }

    // ---------- RBAC (matriz de permisos) ----------

    [FactIntegracion]
    public async Task SoloElIngenieroCargaYDesactiva_LaDirectivaNoConsulta()
    {
        var i = await InstitucionConEquipo();
        var id = (await Cuerpo(await Adjuntar(i.Ingeniero, i.Equipo, "Ficha técnica", "ficha.pdf", Pdf))).GetProperty("id").GetString();

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var cliente = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await Adjuntar(cliente, i.Equipo, "Otro", "otro.pdf", Pdf)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.DeleteAsync($"{Ruta}/{i.Equipo}/documentos/{id}")).StatusCode);
        }

        var directiva = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);
        Assert.Equal(HttpStatusCode.Forbidden, (await directiva.GetAsync($"{Ruta}/{i.Equipo}/documentos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Ruta}/{i.Equipo}/documentos")).StatusCode);
        Assert.Single(await api.LeerDocumentosAsync(i.Equipo));
    }

    // ---------- Aislamiento entre tenants (SEC-003) ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoVeNiDescargaNiCargaNiDesactivaDocumentos()
    {
        var a = await InstitucionConEquipo();
        var b = await InstitucionConEquipo();
        var id = (await Cuerpo(await Adjuntar(a.Ingeniero, a.Equipo, "Manual de usuario", "manual.pdf", Pdf))).GetProperty("id").GetString();
        var consultaB = await SesionDe(b.Tenant, CodigosRol.AdministradorSistema);

        Assert.Equal(HttpStatusCode.NotFound, (await consultaB.GetAsync($"{Ruta}/{a.Equipo}/documentos")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await consultaB.GetAsync($"{Ruta}/{a.Equipo}/documentos/{id}/archivo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{b.Equipo}/documentos/{id}/archivo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Adjuntar(b.Ingeniero, a.Equipo, "Otro", "intruso.pdf", Pdf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.DeleteAsync($"{Ruta}/{a.Equipo}/documentos/{id}")).StatusCode);
        Assert.Empty(await Listar(b.Ingeniero, b.Equipo));

        var documento = Assert.Single(await api.LeerDocumentosAsync(a.Equipo));
        Assert.True(documento.EstadoActivo);
        Assert.False(Directory.Exists(Path.Combine(api.RaizAdjuntos, b.Tenant.ToString(), a.Equipo.ToString())));
    }

    // ---------- RN-009: almacenamiento del plan ----------

    [FactIntegracion]
    public async Task AlmacenamientoDelPlanLleno_RechazaConMsg019YLaConsultaSigue()
    {
        var i = await InstitucionConEquipo("Consultorio", almacenamientoGb: 5);
        await api.OcuparAlmacenamientoAsync(i.Tenant, i.Equipo, i.IngenieroId, (5L * 1024 * 1024 * 1024) - 10);

        var rechazo = await Adjuntar(i.Ingeniero, i.Equipo, "Ficha técnica", "ficha.pdf", Pdf);

        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        var cuerpo = await Cuerpo(rechazo);
        Assert.Equal(GestionDocumentos.CodigoLimitePlan, cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("máximo de almacenamiento del plan Consultorio (5 GB)", cuerpo.GetProperty("detail").GetString());
        Assert.Single(await Listar(i.Ingeniero, i.Equipo));
    }

    // ---------- Desactivación lógica ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task Desactivar_DejaDeMostrarseConservaElArchivoYQuedaEnLaBitacora()
    {
        var i = await InstitucionConEquipo();
        var id = Guid.Parse((await Cuerpo(await Adjuntar(i.Ingeniero, i.Equipo, "Certificado de calibración", "certificado.pdf", Pdf))).GetProperty("id").GetString()!);

        var respuesta = await i.Ingeniero.DeleteAsync($"{Ruta}/{i.Equipo}/documentos/{id}");

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Empty(await Listar(i.Ingeniero, i.Equipo));
        Assert.Equal(HttpStatusCode.NotFound, (await i.Ingeniero.GetAsync($"{Ruta}/{i.Equipo}/documentos/{id}/archivo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await i.Ingeniero.DeleteAsync($"{Ruta}/{i.Equipo}/documentos/{id}")).StatusCode);

        var guardado = Assert.Single(await api.LeerDocumentosAsync(i.Equipo));
        Assert.Equal((false, (Guid?)i.IngenieroId), (guardado.EstadoActivo, guardado.DesactivadoPorId));
        Assert.NotNull(guardado.FechaDesactivacion);
        Assert.True(File.Exists(Path.Combine(ArchivosDe(i), $"{id:N}.pdf")));

        var evento = (await api.LeerBitacoraAsync(i.Tenant)).First(d => d["accion"] == "ELIMINAR" && d["entidad"] == GestionDocumentos.Entidad);
        Assert.Equal(id.ToString(), evento["entidad_id"].AsString);
        Assert.Equal(i.Equipo.ToString(), evento["detalle"]["equipo_id"].AsString);
    }
}
