using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-012 (DOC-04) contra la API y PostgreSQL reales: los
/// usuarios inician sesión de verdad y cada revocación se comprueba en <c>sesion_token</c>.
/// El cierre automático por inactividad en WPF (resto de CA-HU-012-01) es del commit 20.
/// </summary>
public class CriteriosHu012Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Auth = "/api/v1/auth";
    private const string Usuarios = "/api/v1/usuarios";

    /// <summary>Administrador nuevo en el tenant A con sesión iniciada: devuelve su token y su jti.</summary>
    private async Task<(string Token, string Jti)> SesionDeAdministrador(string nombre)
    {
        var correo = api.Correo(nombre);
        await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.AdministradorSistema);
        var (estado, cuerpo) = await api.LoginAsync(correo, ApiConBaseRealFixture.Clave);
        Assert.Equal(HttpStatusCode.OK, estado);
        var token = cuerpo.GetProperty("token").GetString()!;
        return (token, new JsonWebToken(token).Id);
    }

    private HttpClient Con(string token)
    {
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    [FactIntegracion]
    public async Task CA_HU_012_02_CerrarSesion_RevocaElTokenYNoPuedeReutilizarse()
    {
        var (token, jti) = await SesionDeAdministrador("logout");
        var cliente = Con(token);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync(Usuarios)).StatusCode);

        var cierre = await cliente.PostAsync($"{Auth}/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, cierre.StatusCode);
        var sesion = await api.LeerSesionAsync(jti);
        Assert.NotNull(sesion?.FechaRevocacion);
        Assert.Equal(MotivosRevocacion.CierreDeSesion, sesion!.MotivoRevocacion);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Usuarios)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsync($"{Auth}/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsync($"{Auth}/logout", null)).StatusCode);
        Assert.Equal(sesion.FechaRevocacion, (await api.LeerSesionAsync(jti))!.FechaRevocacion);
    }

    [FactIntegracion]
    public async Task CA_HU_012_01_CierrePorInactividad_GuardaElMotivoEInvalidaElToken()
    {
        var (token, jti) = await SesionDeAdministrador("inactivo");
        var cliente = Con(token);

        var cierre = await cliente.PostAsJsonAsync($"{Auth}/logout", new { motivo = "inactividad" });

        Assert.Equal(HttpStatusCode.NoContent, cierre.StatusCode);
        Assert.Equal(MotivosRevocacion.Inactividad, (await api.LeerSesionAsync(jti))?.MotivoRevocacion);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Usuarios)).StatusCode);
    }

    [FactIntegracion]
    public async Task Renovar_EntregaOtroJtiYElTokenAnteriorQuedaRevocado()
    {
        var (token, jti) = await SesionDeAdministrador("renueva");
        var anterior = Con(token);

        var respuesta = await anterior.PostAsync($"{Auth}/refresh", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var nuevo = cuerpo.RootElement.GetProperty("token").GetString()!;
        var jtiNuevo = new JsonWebToken(nuevo).Id;
        Assert.NotEqual(jti, jtiNuevo);
        Assert.Equal("AC-01", cuerpo.RootElement.GetProperty("rol").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Con(nuevo).GetAsync(Usuarios)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anterior.GetAsync(Usuarios)).StatusCode);

        var revocada = await api.LeerSesionAsync(jti);
        var registrada = await api.LeerSesionAsync(jtiNuevo);
        Assert.Equal(MotivosRevocacion.Renovacion, revocada?.MotivoRevocacion);
        Assert.NotNull(registrada);
        Assert.Null(registrada!.FechaRevocacion);
        Assert.Equal((revocada!.TenantId, revocada.UsuarioId), (registrada.TenantId, registrada.UsuarioId));
    }

    [FactIntegracion]
    public async Task TokenExpirado_ConSuSesionSinRevocar_Responde401()
    {
        var correo = api.Correo("vencido");
        var usuarioId = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.AdministradorSistema);
        var vencido = await api.TokenConSesionAsync(usuarioId, api.TenantA, CodigosRol.AdministradorSistema, DateTimeOffset.UtcNow.AddMinutes(-40));
        var cliente = Con(vencido);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Usuarios)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsync($"{Auth}/refresh", null)).StatusCode);
        Assert.Null((await api.LeerSesionAsync(new JsonWebToken(vencido).Id))?.FechaRevocacion);
    }

    [FactIntegracion]
    public async Task CuentaDesactivadaDuranteLaSesion_NoPuedeRenovarYSuSesionSeCierra()
    {
        var (tokenAdmin, _) = await SesionDeAdministrador("admin-desactiva");
        var correo = api.Correo("tecnico-desactivado");
        var tecnico = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);
        var (_, cuerpo) = await api.LoginAsync(correo, ApiConBaseRealFixture.Clave);
        var tokenTecnico = cuerpo.GetProperty("token").GetString()!;
        var desactivar = await Con(tokenAdmin).PatchAsJsonAsync($"{Usuarios}/{tecnico}/estado", new { accion = "desactivar" });
        Assert.Equal(HttpStatusCode.OK, desactivar.StatusCode);

        var respuesta = await Con(tokenTecnico).PostAsync($"{Auth}/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var error = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("MSG-002", error.RootElement.GetProperty("codigo").GetString());
        Assert.Equal(MotivosRevocacion.AccesoNoDisponible, (await api.LeerSesionAsync(new JsonWebToken(tokenTecnico).Id))?.MotivoRevocacion);
    }

    [FactIntegracion]
    public async Task JtiDeUnaSesionDeOtraInstitucion_Responde401()
    {
        var usuarioA = await api.CrearUsuarioAsync(api.TenantA, api.Correo("dueno-a"), CodigosRol.AdministradorSistema);
        var usuarioB = await api.CrearUsuarioAsync(api.TenantB, api.Correo("dueno-b"), CodigosRol.AdministradorSistema);

        // Firma válida y vigente con el tenant A, pero el jti pertenece a una sesión del tenant B.
        var cruzado = await api.TokenConSesionAsync(usuarioA, api.TenantA, CodigosRol.AdministradorSistema, DateTimeOffset.UtcNow, api.TenantB, usuarioB);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Con(cruzado).GetAsync(Usuarios)).StatusCode);
    }
}
