using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-001 (DOC-04) contra la API y PostgreSQL reales.
/// El menú por rol en WPF (parte de CA-HU-001-01) se prueba con el cliente (commits 10 y 11).
/// </summary>
public class CriteriosHu001Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/auth/login";

    private async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Login(string correo, string clave)
    {
        var respuesta = await api.CreateClient().PostAsJsonAsync(Ruta, new { correo, contrasena = clave });
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
        return (respuesta.StatusCode, cuerpo);
    }

    [FactIntegracion]
    public async Task CA_HU_001_01_CredencialesValidas_EmiteJwtConTenantYRolYRegistraLaSesion()
    {
        var correo = api.Correo("ingeniero");
        var usuarioId = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.IngenieroBiomedico);

        var (estado, cuerpo) = await Login(correo, ApiConBaseRealFixture.Clave);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("AC-02", cuerpo.GetProperty("rol").GetString());
        var token = new JsonWebToken(cuerpo.GetProperty("token").GetString());
        Assert.Equal(api.TenantA.ToString(), token.GetClaim(ClaimsSitbio.Tenant).Value);
        Assert.Equal(usuarioId.ToString(), token.GetClaim(ClaimsSitbio.Usuario).Value);
        Assert.Equal("AC-02", token.GetClaim(ClaimsSitbio.Rol).Value);
        Assert.Equal(TimeSpan.FromMinutes(30), token.ValidTo - token.IssuedAt);
        Assert.True(await api.ExisteSesionAsync(token.Id));
        Assert.NotNull(await api.LeerUsuarioAsync(usuarioId, u => u.UltimoAcceso));
    }

    [FactIntegracion]
    public async Task CA_HU_001_01_UsuarioDeOtroTenant_RecibeSuPropioTenant()
    {
        var correo = api.Correo("usuario-b");
        await api.CrearUsuarioAsync(api.TenantB, correo, CodigosRol.TecnicoMantenimiento);

        var (estado, cuerpo) = await Login(correo, ApiConBaseRealFixture.Clave);

        Assert.Equal(HttpStatusCode.OK, estado);
        var token = new JsonWebToken(cuerpo.GetProperty("token").GetString());
        Assert.Equal(api.TenantB.ToString(), token.GetClaim(ClaimsSitbio.Tenant).Value);
        Assert.Equal("AC-03", token.GetClaim(ClaimsSitbio.Rol).Value);
    }

    [FactIntegracion]
    public async Task CA_HU_001_02_CredencialesInvalidas_MuestraMsg001SinDetallarElFallo()
    {
        var correo = api.Correo("tecnico-02");
        await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);

        var (estado, cuerpo) = await Login(correo, "clave-errada");

        Assert.Equal(HttpStatusCode.Unauthorized, estado);
        Assert.Equal("MSG-001", cuerpo.GetProperty("codigo").GetString());
        Assert.Equal("Credenciales incorrectas. Verifique sus datos e intente de nuevo.", cuerpo.GetProperty("detail").GetString());
        var texto = cuerpo.GetRawText().ToLowerInvariant();
        foreach (var interno in new[] { "exception", "select", "bcrypt", "bloque", "stack", correo })
        {
            Assert.DoesNotContain(interno, texto);
        }
    }

    [FactIntegracion]
    public async Task CA_HU_001_02_CorreoInexistente_RespondeIgualQueUnaClaveErrada()
    {
        var correo = api.Correo("tecnico-existe");
        await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);

        var (estadoExiste, existe) = await Login(correo, "clave-errada");
        var (estadoNoExiste, noExiste) = await Login(api.Correo("no-existe"), "clave-errada");

        Assert.Equal(estadoExiste, estadoNoExiste);
        Assert.Equal(SinTrazas(existe), SinTrazas(noExiste));
    }

    [FactIntegracion]
    public async Task CA_HU_001_03_CincoFallos_BloqueanLaCuentaYElSextoIntentoNoEntra()
    {
        var correo = api.Correo("tecnico-03");
        var usuarioId = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);
        var otro = api.Correo("tecnico-sin-bloqueo");
        await api.CrearUsuarioAsync(api.TenantA, otro, CodigosRol.TecnicoMantenimiento);

        for (var i = 1; i <= Usuario.MaximoIntentosFallidos; i++)
        {
            var (estadoFallo, fallo) = await Login(correo, "clave-errada");
            Assert.Equal(HttpStatusCode.Unauthorized, estadoFallo);
            Assert.Equal("MSG-001", fallo.GetProperty("codigo").GetString());
        }

        var (estado, cuerpo) = await Login(correo, ApiConBaseRealFixture.Clave);

        Assert.Equal(HttpStatusCode.Unauthorized, estado);
        Assert.Equal("MSG-002", cuerpo.GetProperty("codigo").GetString());
        Assert.True(await api.LeerUsuarioAsync(usuarioId, u => u.EstadoBloqueo));
        Assert.Equal(HttpStatusCode.OK, (await Login(otro, ApiConBaseRealFixture.Clave)).Estado);

        // «Solo el Administrador puede reactivarla»: el desbloqueo es HU-011 (commit 13).
    }

    [FactIntegracion]
    public async Task CA_HU_001_03_LosIntentosSonConsecutivos_UnAccesoCorrectoReiniciaElConteo()
    {
        var correo = api.Correo("tecnico-consecutivo");
        var usuarioId = await api.CrearUsuarioAsync(api.TenantA, correo, CodigosRol.TecnicoMantenimiento);

        for (var i = 1; i < Usuario.MaximoIntentosFallidos; i++)
        {
            await Login(correo, "clave-errada");
        }

        Assert.Equal(HttpStatusCode.OK, (await Login(correo, ApiConBaseRealFixture.Clave)).Estado);
        for (var i = 1; i < Usuario.MaximoIntentosFallidos; i++)
        {
            await Login(correo, "clave-errada");
        }

        Assert.False(await api.LeerUsuarioAsync(usuarioId, u => u.EstadoBloqueo));
        Assert.Equal(Usuario.MaximoIntentosFallidos - 1, await api.LeerUsuarioAsync(usuarioId, u => (int)u.IntentosFallidos));
    }

    private static string SinTrazas(JsonElement cuerpo)
    {
        var nodo = JsonNode.Parse(cuerpo.GetRawText())!.AsObject();
        nodo.Remove("traceId");
        nodo.Remove("correlationId");
        return nodo.ToJsonString();
    }
}
