using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

public class ContextoUsuarioHttpTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private static ContextoUsuarioHttp Contexto(ClaimsPrincipal usuario) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = usuario } });

    private static ClaimsPrincipal Autenticado(params (string Tipo, string Valor)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Tipo, c.Valor)), "Bearer"));

    [Fact]
    public void LeeTenantUsuarioYRolDelToken()
    {
        var contexto = Contexto(Autenticado(
            (ClaimsSitbio.Tenant, Tenant.ToString()),
            (ClaimsSitbio.Usuario, UsuarioId.ToString()),
            (ClaimsSitbio.Rol, CodigosRol.TecnicoMantenimiento)));

        Assert.Equal(Tenant, contexto.TenantId);
        Assert.Equal(UsuarioId, contexto.UsuarioId);
        Assert.Equal("AC-03", contexto.Rol);
    }

    [Fact]
    public void SinAutenticacion_LanzaErrorNoAutenticado()
    {
        var contexto = Contexto(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Throws<ErrorNoAutenticado>(() => contexto.TenantId);
        Assert.Throws<ErrorNoAutenticado>(() => contexto.Rol);
    }

    [Fact]
    public void SinTenant_LanzaErrorNoAutenticado()
    {
        var contexto = Contexto(Autenticado((ClaimsSitbio.Usuario, UsuarioId.ToString()), (ClaimsSitbio.Rol, "AC-03")));

        Assert.Throws<ErrorNoAutenticado>(() => contexto.TenantId);
    }

    [Theory]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TenantInvalido_LanzaErrorNoAutenticado(string tenant)
    {
        var contexto = Contexto(Autenticado((ClaimsSitbio.Tenant, tenant)));

        Assert.Throws<ErrorNoAutenticado>(() => contexto.TenantId);
    }

    [Fact]
    public void Establecer_SinToken_FijaLaIdentidadDelServidor()
    {
        var contexto = Contexto(new ClaimsPrincipal(new ClaimsIdentity()));

        contexto.Establecer(Tenant, UsuarioId, "AC-03");

        Assert.Equal(Tenant, contexto.TenantId);
        Assert.Equal(UsuarioId, contexto.UsuarioId);
        Assert.Equal("AC-03", contexto.Rol);
    }

    [Fact]
    public void Establecer_OtraInstitucion_Lanza()
    {
        var contexto = Contexto(new ClaimsPrincipal(new ClaimsIdentity()));
        contexto.Establecer(Tenant, UsuarioId, "AC-03");

        Assert.Throws<InvalidOperationException>(() => contexto.Establecer(Guid.NewGuid(), UsuarioId, "AC-03"));
    }

    [Fact]
    public async Task DePuntaAPunta_LoQueEmiteElGeneradorEsLoQueLeeElContexto()
    {
        var opciones = new OpcionesJwt
        {
            Issuer = "sitbio-api",
            Audience = "sitbio-desktop",
            Clave = "clave-de-pruebas-con-mas-de-32-bytes!!",
            MinutosVigencia = 30,
        };
        var usuario = new Usuario(Tenant, 2, "Iván", "Gómez", "ivan.gomez@demo-a.co", "hash");
        var emitido = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(usuario, CodigosRol.IngenieroBiomedico);

        var validado = await new JsonWebTokenHandler().ValidateTokenAsync(emitido.Token, ParametrosValidacionJwt.Crear(opciones));
        var contexto = Contexto(new ClaimsPrincipal(validado.ClaimsIdentity));

        Assert.True(validado.IsValid);
        Assert.Equal(Tenant, contexto.TenantId);
        Assert.Equal(usuario.Id, contexto.UsuarioId);
        Assert.Equal("AC-02", contexto.Rol);
    }
}
