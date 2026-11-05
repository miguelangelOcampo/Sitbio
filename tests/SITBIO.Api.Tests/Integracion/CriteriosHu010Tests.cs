using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-010 (DOC-04) contra la API, PostgreSQL y MongoDB reales: los
/// usuarios inician sesión de verdad y cada evento se comprueba en <c>bitacora_auditoria</c>,
/// que tiene el validador real (02 + V007). La pantalla UI-024 es del commit 24.
/// </summary>
public class CriteriosHu010Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Bitacora = "/api/v1/bitacora";
    private const string Usuarios = "/api/v1/usuarios";

    private async Task<(Guid Id, string Correo, HttpClient Cliente)> ConSesion(Guid tenant, string nombre, string rol)
    {
        var correo = api.Correo(nombre);
        var id = await api.CrearUsuarioAsync(tenant, correo, rol);
        return (id, correo, await api.IniciarSesionAsync(correo));
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<List<BsonDocument>> EventosDe(Guid tenant, Guid usuario, string accion) =>
        (await api.LeerBitacoraAsync(tenant)).Where(d => d["usuario_id"] == usuario.ToString() && d["accion"] == accion).ToList();

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_010_01_UnaAccionSinPermiso_Responde403YQuedaEnLaBitacora()
    {
        var (ingeniero, correo, cliente) = await ConSesion(api.TenantA, "h10-ingeniero", CodigosRol.IngenieroBiomedico);
        var antes = DateTime.UtcNow.AddSeconds(-5);

        var respuesta = await cliente.PostAsJsonAsync(Usuarios, new { nombres = "X", apellidos = "Y", correo = api.Correo("h10-intruso"), rol = "AC-03" });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var evento = Assert.Single(await EventosDe(api.TenantA, ingeniero, "ACCESO_DENEGADO"));
        Assert.Equal("usuarios", evento["entidad"].AsString);
        Assert.Equal("POST", evento["detalle"]["metodo"].AsString);
        Assert.Equal(Usuarios, evento["detalle"]["ruta"].AsString);
        Assert.Equal("AC-02", evento["detalle"]["rol"].AsString);
        Assert.InRange(evento["fecha_hora"].ToUniversalTime(), antes, DateTime.UtcNow.AddSeconds(5));
        // ip_origen no aparece aquí: TestServer no tiene IP remota y el campo vacío se omite
        // (el validador rechaza null). La IP se prueba en RegistroAuditoriaTests.

        // Sin datos personales en el registro: ni el correo del usuario ni el del formulario.
        var texto = evento.ToJson();
        Assert.DoesNotContain(correo, texto);
        Assert.DoesNotContain("h10-intruso", texto);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_010_02_CadaAdministradorSoloVeLaBitacoraDeSuInstitucion()
    {
        var (adminA, _, clienteA) = await ConSesion(api.TenantA, "h10-admin-a", CodigosRol.AdministradorSistema);
        var (adminB, _, clienteB) = await ConSesion(api.TenantB, "h10-admin-b", CodigosRol.AdministradorSistema);

        var deB = await Cuerpo(await clienteB.GetAsync($"{Bitacora}?pageSize=100"));
        var cruzado = await Cuerpo(await clienteB.GetAsync($"{Bitacora}?usuarioId={adminA}"));

        var usuariosVistosPorB = deB.GetProperty("items").EnumerateArray().Select(e => Guid.Parse(e.GetProperty("usuarioId").GetString()!)).ToList();
        Assert.Contains(adminB, usuariosVistosPorB);
        Assert.DoesNotContain(adminA, usuariosVistosPorB);
        Assert.Equal(0, cruzado.GetProperty("total").GetInt32());

        var deA = await Cuerpo(await clienteA.GetAsync($"{Bitacora}?usuarioId={adminA}"));
        Assert.True(deA.GetProperty("total").GetInt32() >= 1);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_010_03_AdministradorYAuditor_FiltranPorUsuarioFechaYAccion()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (_, _, admin) = await ConSesion(tenant, "h10-admin", CodigosRol.AdministradorSistema);
        var (_, _, auditor) = await ConSesion(tenant, "h10-auditor", CodigosRol.EnteRegulador);
        var correoTecnico = api.Correo("h10-tecnico");
        var tecnico = await api.CrearUsuarioAsync(tenant, correoTecnico, CodigosRol.TecnicoMantenimiento);
        var desde = DateTimeOffset.UtcNow.AddMinutes(-1);
        await api.LoginAsync(correoTecnico, "clave-errada");
        var clienteTecnico = await api.IniciarSesionAsync(correoTecnico);
        await clienteTecnico.PostAsJsonAsync("/api/v1/auth/logout", new { motivo = "inactividad" });
        var hasta = DateTimeOffset.UtcNow.AddMinutes(1);

        foreach (var cliente in new[] { admin, auditor })
        {
            var respuesta = await cliente.GetAsync(
                $"{Bitacora}?usuarioId={tecnico}&desde={Uri.EscapeDataString(desde.ToString("O"))}&hasta={Uri.EscapeDataString(hasta.ToString("O"))}");

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var items = (await Cuerpo(respuesta)).GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(["CIERRE_SESION", "LOGIN_OK", "LOGIN_FALLIDO"], items.Select(e => e.GetProperty("accion").GetString()));
            Assert.All(items, e => Assert.Equal("Prueba HU-001", e.GetProperty("usuario").GetString()));
            Assert.Equal("inactividad", items[0].GetProperty("detalle").GetProperty("motivo").GetString());
            Assert.Equal("credenciales", items[2].GetProperty("detalle").GetProperty("motivo").GetString());

            var soloFallidos = await Cuerpo(await cliente.GetAsync($"{Bitacora}?usuarioId={tecnico}&accion=LOGIN_FALLIDO"));
            Assert.Equal(1, soloFallidos.GetProperty("total").GetInt32());
        }
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_010_03_UnTecnicoNoConsultaLaBitacoraYElIntentoQuedaRegistrado()
    {
        var (tecnico, _, cliente) = await ConSesion(api.TenantA, "h10-tecnico-curioso", CodigosRol.TecnicoMantenimiento);

        var respuesta = await cliente.GetAsync(Bitacora);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var evento = Assert.Single(await EventosDe(api.TenantA, tecnico, "ACCESO_DENEGADO"));
        Assert.Equal("bitacora", evento["entidad"].AsString);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task LoginYCierreDeSesion_QuedanEnLaBitacoraDelTenant()
    {
        var (usuario, correo, cliente) = await ConSesion(api.TenantB, "h10-sesion", CodigosRol.IngenieroBiomedico);

        await cliente.PostAsync("/api/v1/auth/logout", null);

        var eventos = (await api.LeerBitacoraAsync(api.TenantB)).Where(d => d["usuario_id"] == usuario.ToString()).ToList();
        Assert.Equal(["CIERRE_SESION", "LOGIN_OK"], eventos.Select(d => d["accion"].AsString));
        Assert.Equal("voluntario", eventos[0]["detalle"]["motivo"].AsString);
        Assert.Equal(usuario.ToString(), eventos[1]["entidad_id"].AsString);
        Assert.DoesNotContain(correo, eventos.ToJson());
    }
}
