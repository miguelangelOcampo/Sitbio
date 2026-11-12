using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Application.Auditoria;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-006 (DOC-04, CP-009 y CP-010) contra la API, PostgreSQL y MongoDB
/// reales: los usuarios inician sesión de verdad, el tipo de equipo se crea con API-CAT y cada versión
/// de campos se comprueba en <c>configuracion_campos</c> con el validador del script 02. Cada prueba
/// usa su propia institución.
/// </summary>
public class CriteriosHu006Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/configuracion-campos";

    private static readonly object TasaDeGoteo = new { etiqueta = "Tasa de goteo", tipoDato = TiposDatoCampo.NumeroDecimal, obligatorio = true, unidad = "ml/h" };
    private static readonly object Oclusion = new { etiqueta = "Prueba de oclusión", tipoDato = TiposDatoCampo.Seleccion, obligatorio = true, opciones = new[] { "Pasa", "No pasa" } };

    // ---------- CA-HU-006-01 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_006_01_CampoNuevo_LoRecibeElTecnicoParaElFormularioDeEseTipo()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var tecnico = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");
        var monitor = await CrearTipo(ingeniero, "Monitor de signos vitales");

        var publicada = await Publicar(ingeniero, bomba, TasaDeGoteo);
        Assert.Equal(1, publicada.GetProperty("version").GetInt32());

        // El técnico recibe la definición con la que la app WPF arma el formulario de intervención.
        var configuracion = await Consultar(tecnico, bomba);
        Assert.Equal(1, configuracion.GetProperty("version").GetInt32());
        var campo = Assert.Single(configuracion.GetProperty("campos").EnumerateArray());
        Assert.Equal("tasa_de_goteo", campo.GetProperty("nombre").GetString());
        Assert.Equal("Tasa de goteo", campo.GetProperty("etiqueta").GetString());
        Assert.Equal(TiposDatoCampo.NumeroDecimal, campo.GetProperty("tipoDato").GetString());
        Assert.True(campo.GetProperty("obligatorio").GetBoolean());
        Assert.Equal("ml/h", campo.GetProperty("unidad").GetString());

        // Solo para ese tipo de equipo.
        var otroTipo = await Consultar(tecnico, monitor);
        Assert.Equal(JsonValueKind.Null, otroTipo.GetProperty("version").ValueKind);
        Assert.Empty(otroTipo.GetProperty("campos").EnumerateArray());

        // En MongoDB queda la versión activa con el tenant y el tipo (pasa el $jsonSchema del script 02).
        var documento = Assert.Single(await api.LeerConfiguracionCamposAsync(tenant));
        Assert.Equal(bomba.ToString(), documento["tipo_equipo"].AsString);
        Assert.Equal(BsonType.Int32, documento["version"].BsonType);
        Assert.Equal(EstadosConfiguracion.Activa, documento["estado"].AsString);
        Assert.Equal("tasa_de_goteo", documento["campos"][0]["nombre"].AsString);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_006_01_NuevaVersion_ReemplazaLaActivaYConservaLaAnterior()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var tecnico = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");
        await Publicar(ingeniero, bomba, TasaDeGoteo);

        var segunda = await Publicar(ingeniero, bomba, TasaDeGoteo, Oclusion);

        Assert.Equal(2, segunda.GetProperty("version").GetInt32());
        var configuracion = await Consultar(tecnico, bomba);
        Assert.Equal(["tasa_de_goteo", "prueba_de_oclusion"], configuracion.GetProperty("campos").EnumerateArray().Select(c => c.GetProperty("nombre").GetString()));
        Assert.Equal(
            [(2, EstadosConfiguracion.Activa), (1, EstadosConfiguracion.Historica)],
            configuracion.GetProperty("versiones").EnumerateArray().Select(v => (v.GetProperty("version").GetInt32(), v.GetProperty("estado").GetString())));

        // Las intervenciones ya registradas conservan su versión: la anterior no se borra ni se edita.
        var documentos = await api.LeerConfiguracionCamposAsync(tenant);
        Assert.Equal([2, 1], documentos.Select(d => d["version"].AsInt32));
        Assert.Single(documentos[1]["campos"].AsBsonArray);

        // Publicar lo mismo otra vez no crea otra versión.
        var repetida = await ingeniero.PostAsJsonAsync($"{Ruta}/{bomba}", new { campos = new[] { TasaDeGoteo, Oclusion } });
        Assert.Equal(HttpStatusCode.BadRequest, repetida.StatusCode);
        Assert.Equal(2, (await api.LeerConfiguracionCamposAsync(tenant)).Count);

        // La publicación queda en la bitácora con el tipo y la versión.
        var evento = (await api.LeerBitacoraAsync(tenant)).First(e => e["entidad"] == GestionConfiguracionCampos.Entidad);
        Assert.Equal(AccionesAuditoria.Crear, evento["accion"].AsString);
        Assert.Equal(bomba.ToString(), evento["entidad_id"].AsString);
        Assert.Equal("2", evento["detalle"]["version"].AsString);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_006_01_CamposInvalidos_Responde400SinGuardar()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");

        var respuesta = await ingeniero.PostAsJsonAsync($"{Ruta}/{bomba}", new
        {
            campos = new object[]
            {
                new { etiqueta = "Resultado", tipoDato = TiposDatoCampo.Seleccion, opciones = new[] { "Pasa" } },
                new { etiqueta = "Tasa", tipoDato = "moneda" },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var errores = (await Cuerpo(respuesta)).GetProperty("errors");
        Assert.True(errores.TryGetProperty("campos[0].opciones", out _));
        Assert.True(errores.TryGetProperty("campos[1].tipoDato", out _));
        Assert.Empty(await api.LeerConfiguracionCamposAsync(tenant));
    }

    // ---------- CA-HU-006-02 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_006_02_OtraInstitucion_NoVeNiModificaLosCampos()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var (otroTenant, ingenieroAjeno) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var tecnicoAjeno = await SesionDe(otroTenant, CodigosRol.TecnicoMantenimiento);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");
        await Publicar(ingeniero, bomba, TasaDeGoteo);

        // El tipo de otra institución no existe para ellos (SEC-003).
        Assert.Equal(HttpStatusCode.NotFound, (await tecnicoAjeno.GetAsync($"{Ruta}/{bomba}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ingenieroAjeno.GetAsync($"{Ruta}/{bomba}")).StatusCode);
        var publicarAjeno = await ingenieroAjeno.PostAsJsonAsync($"{Ruta}/{bomba}", new { campos = new[] { Oclusion } });
        Assert.Equal(HttpStatusCode.NotFound, publicarAjeno.StatusCode);

        // Su propio tipo con el mismo nombre no trae los campos de la primera institución.
        var bombaAjena = await CrearTipo(ingenieroAjeno, "Bomba de infusión");
        Assert.Empty((await Consultar(tecnicoAjeno, bombaAjena)).GetProperty("campos").EnumerateArray());

        Assert.Empty(await api.LeerConfiguracionCamposAsync(otroTenant));
        Assert.Single(await api.LeerConfiguracionCamposAsync(tenant));
    }

    // ---------- CA-HU-006-03 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_006_03_Administrador_SoloConsulta()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var administrador = await SesionDe(tenant, CodigosRol.AdministradorSistema);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");
        await Publicar(ingeniero, bomba, TasaDeGoteo);

        var configuracion = await Consultar(administrador, bomba);
        Assert.Equal("tasa_de_goteo", Assert.Single(configuracion.GetProperty("campos").EnumerateArray()).GetProperty("nombre").GetString());

        var publicar = await administrador.PostAsJsonAsync($"{Ruta}/{bomba}", new { campos = new[] { TasaDeGoteo, Oclusion } });

        Assert.Equal(HttpStatusCode.Forbidden, publicar.StatusCode);
        Assert.Single(await api.LeerConfiguracionCamposAsync(tenant));
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task Permisos_OtrosRolesReciben403YSinToken401()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var bomba = await CrearTipo(ingeniero, "Bomba de infusión");
        var tecnico = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);

        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync($"{Ruta}/{bomba}", new { campos = new[] { TasaDeGoteo } })).StatusCode);
        foreach (var rol in new[] { CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.DirectivaInstitucional })
        {
            var cliente = await SesionDe(tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/{bomba}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Ruta}/{bomba}")).StatusCode);
        Assert.Empty(await api.LeerConfiguracionCamposAsync(tenant));
    }

    // ---------- Apoyo ----------

    private async Task<(Guid Tenant, HttpClient Cliente)> InstitucionCon(string codigoRol)
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        return (tenant, await SesionDe(tenant, codigoRol));
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    /// <summary>Tipo de equipo creado por API-CAT (HU-025), como lo hace el Ingeniero Biomédico.</summary>
    private static async Task<Guid> CrearTipo(HttpClient ingeniero, string nombre)
    {
        var respuesta = await ingeniero.PostAsJsonAsync("/api/v1/catalogos/tipos-equipo", new { nombre, requiereCalibracion = true, periodicidadCalibracionMeses = 12 });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Cuerpo(respuesta)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Publicar(HttpClient ingeniero, Guid tipo, params object[] campos)
    {
        var respuesta = await ingeniero.PostAsJsonAsync($"{Ruta}/{tipo}", new { campos });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.EndsWith($"/configuracion-campos/{tipo}", respuesta.Headers.Location!.ToString());
        return await Cuerpo(respuesta);
    }

    private static async Task<JsonElement> Consultar(HttpClient cliente, Guid tipo)
    {
        var respuesta = await cliente.GetAsync($"{Ruta}/{tipo}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
