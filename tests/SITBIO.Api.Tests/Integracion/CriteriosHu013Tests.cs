using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using MongoDB.Bson;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-013 (DOC-04) contra la API, PostgreSQL y MongoDB reales: el
/// Administrador emite una clave temporal, el usuario entra con ella y solo puede cambiarla
/// (MSG-004) hasta definir una nueva que cumpla RF-012. Cada prueba usa su propio usuario.
/// </summary>
public class CriteriosHu013Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Auth = "/api/v1/auth";
    private const string Usuarios = "/api/v1/usuarios";
    private const string ClaveNueva = "Monitor#2026uci";

    private sealed record Sesion(HttpClient Cliente, string Jti);

    private async Task<Sesion> Entrar(string correo, string clave)
    {
        var (estado, cuerpo) = await api.LoginAsync(correo, clave);
        Assert.Equal(HttpStatusCode.OK, estado);
        var token = cuerpo.GetProperty("token").GetString()!;
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Sesion(cliente, new JsonWebToken(token).Id);
    }

    /// <summary>Técnico nuevo en el tenant A; el Administrador le emite una clave temporal y la devuelve.</summary>
    private async Task<(Guid Usuario, string Correo, string Temporal, Sesion Anterior)> TecnicoConClaveTemporal(string nombre)
    {
        var correo = api.Correo(nombre);
        var usuario = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);
        var anterior = await Entrar(correo, ApiConBaseRealFixture.Clave);

        var admin = api.Correo($"{nombre}-admin");
        await api.CrearUsuarioAsync(api.TenantA, admin, CodigosRol.AdministradorSistema);
        var respuesta = await (await api.IniciarSesionAsync(admin)).PostAsync($"{Usuarios}/{usuario}/clave-temporal", null);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var temporal = (await Cuerpo(respuesta)).GetProperty("claveTemporal").GetString()!;
        return (usuario, correo, temporal, anterior);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task<HttpResponseMessage> CambiarClave(HttpClient cliente, string actual, string nueva) =>
        cliente.PutAsJsonAsync($"{Auth}/password", new { actual, nueva });

    private async Task<List<BsonDocument>> EventosDe(Guid usuario, string accion) =>
        (await api.LeerBitacoraAsync(api.TenantA)).Where(d => d["usuario_id"] == usuario.ToString() && d["accion"] == accion).ToList();

    // ---------- CA-HU-013-01 ----------

    [FactIntegracion]
    public async Task CA_HU_013_01_ClaveTemporal_ExigeElCambioYCierraLasSesionesAbiertas()
    {
        var (usuario, correo, temporal, anterior) = await TecnicoConClaveTemporal("h13-emitir");

        Assert.True(await api.LeerUsuarioAsync(usuario, u => u.CambioContrasenaObligatorio));
        var sesion = await api.LeerSesionAsync(anterior.Jti);
        Assert.NotNull(sesion?.FechaRevocacion);
        Assert.Equal(MotivosRevocacion.ClaveTemporal, sesion!.MotivoRevocacion);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anterior.Cliente.GetAsync("/api/v1/equipos")).StatusCode);

        // La clave anterior ya no sirve; la temporal sí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginAsync(correo, ApiConBaseRealFixture.Clave)).Estado);
        var (estado, cuerpo) = await api.LoginAsync(correo, temporal);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.True(cuerpo.GetProperty("debeCambiarClave").GetBoolean());
    }

    // ---------- CA-HU-013-02 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_013_02_ConLaClaveTemporal_SoloPuedeCambiarlaYElIntentoQuedaEnLaBitacora()
    {
        var (usuario, correo, temporal, _) = await TecnicoConClaveTemporal("h13-bloqueo");
        var sesion = await Entrar(correo, temporal);

        var respuesta = await sesion.Cliente.GetAsync("/api/v1/equipos");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(ClaveTemporal.Codigo, cuerpo.GetProperty("codigo").GetString());
        var evento = Assert.Single(await EventosDe(usuario, "ACCESO_DENEGADO"));
        Assert.Equal("/api/v1/equipos", evento["detalle"]["ruta"].AsString);

        // Renovar el token sí se permite mientras tanto.
        Assert.Equal(HttpStatusCode.OK, (await sesion.Cliente.PostAsync($"{Auth}/refresh", null)).StatusCode);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_013_02_AlDefinirUnaNueva_ElMismoTokenSigueYSeEntraConLaNueva()
    {
        var (usuario, correo, temporal, _) = await TecnicoConClaveTemporal("h13-cambio");
        var sesion = await Entrar(correo, temporal);

        var cambio = await CambiarClave(sesion.Cliente, temporal, ClaveNueva);

        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
        Assert.False(await api.LeerUsuarioAsync(usuario, u => u.CambioContrasenaObligatorio));
        Assert.Equal(HttpStatusCode.OK, (await sesion.Cliente.GetAsync("/api/v1/equipos")).StatusCode);
        Assert.Null((await api.LeerSesionAsync(sesion.Jti))!.FechaRevocacion);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginAsync(correo, temporal)).Estado);
        var (estado, cuerpo) = await api.LoginAsync(correo, ClaveNueva);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.False(cuerpo.GetProperty("debeCambiarClave").GetBoolean());

        var evento = Assert.Single(await EventosDe(usuario, "ACTUALIZAR"));
        Assert.Equal("usuario", evento["entidad"].AsString);
        Assert.Equal(CambiarContrasena.DetalleObligatorio, evento["detalle"]["cambio"].AsString);
        Assert.DoesNotContain(ClaveNueva, evento.ToJson());
        Assert.DoesNotContain(temporal, evento.ToJson());
    }

    [FactIntegracion]
    public async Task CA_HU_013_02_UnaNuevaQueNoCumpleLasReglas_Responde400ConCadaReglaYSigueTemporal()
    {
        var (usuario, correo, temporal, _) = await TecnicoConClaveTemporal("h13-reglas");
        var sesion = await Entrar(correo, temporal);

        // El usuario de la fixture se llama «Prueba»: la regla de datos personales la revisa la API.
        var respuesta = await CambiarClave(sesion.Cliente, temporal, "prueba2026");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var reglas = string.Join(" ", (await Cuerpo(respuesta)).GetProperty("errors").GetProperty("nueva").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains(PoliticaContrasena.ReglaMayusculaMinuscula, reglas);
        Assert.Contains(PoliticaContrasena.ReglaSimbolo, reglas);
        Assert.Contains(PoliticaContrasena.ReglaDatosPersonales, reglas);
        Assert.DoesNotContain(PoliticaContrasena.ReglaLongitud, reglas);
        Assert.True(await api.LeerUsuarioAsync(usuario, u => u.CambioContrasenaObligatorio));
    }

    [FactIntegracion]
    public async Task CA_HU_013_02_ConUnaClaveTemporalIncorrecta_Responde400EnActual()
    {
        var (usuario, correo, temporal, _) = await TecnicoConClaveTemporal("h13-actual");
        var sesion = await Entrar(correo, temporal);

        var respuesta = await CambiarClave(sesion.Cliente, "No-Es-La-Temporal-1", ClaveNueva);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.True((await Cuerpo(respuesta)).GetProperty("errors").TryGetProperty("actual", out _));
        Assert.True(await api.LeerUsuarioAsync(usuario, u => u.CambioContrasenaObligatorio));
    }

    // ---------- Cambio voluntario (RF-012) ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CambioVoluntario_CierraLasDemasSesionesYConservaLaActual()
    {
        var correo = api.Correo("h13-voluntario");
        var usuario = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);
        var otra = await Entrar(correo, ApiConBaseRealFixture.Clave);
        var actual = await Entrar(correo, ApiConBaseRealFixture.Clave);

        var respuesta = await CambiarClave(actual.Cliente, ApiConBaseRealFixture.Clave, ClaveNueva);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(MotivosRevocacion.CambioContrasena, (await api.LeerSesionAsync(otra.Jti))!.MotivoRevocacion);
        Assert.Null((await api.LeerSesionAsync(actual.Jti))!.FechaRevocacion);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otra.Cliente.GetAsync("/api/v1/equipos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actual.Cliente.GetAsync("/api/v1/equipos")).StatusCode);

        var evento = Assert.Single(await EventosDe(usuario, "ACTUALIZAR"));
        Assert.Equal(CambiarContrasena.DetalleVoluntario, evento["detalle"]["cambio"].AsString);
    }
}
