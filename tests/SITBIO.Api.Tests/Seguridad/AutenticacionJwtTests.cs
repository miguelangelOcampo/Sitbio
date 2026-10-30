using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sitbio.Api;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

public class AutenticacionJwtTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void JwtBearer_UsaLosParametrosDeLaConfiguracion()
    {
        var conClave = fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Clave", "clave-de-pruebas-con-mas-de-32-bytes!!"));

        var bearer = conClave.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var parametros = bearer.TokenValidationParameters;
        Assert.Equal("sitbio-api", parametros.ValidIssuer);
        Assert.Equal("sitbio-desktop", parametros.ValidAudience);
        Assert.True(parametros.ValidateLifetime);
        Assert.Equal(ClaimsSitbio.Rol, parametros.RoleClaimType);
        Assert.False(bearer.MapInboundClaims);
    }

    [Fact]
    public async Task Salud_SigueSiendoAnonima()
    {
        var respuesta = await fabrica.CreateClient().GetAsync($"{RutasApi.V1}/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinClaveConfigurada_UnTokenNoRompeLasRutasAnonimas()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", "token.de.prueba");

        var respuesta = await cliente.GetAsync($"{RutasApi.V1}/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
