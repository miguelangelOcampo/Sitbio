using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>Contrato HTTP de API-AUTH-REFRESH y API-AUTH-LOGOUT y rechazo de tokens revocados (HU-012), sin base de datos.</summary>
public class SesionEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Auth = $"{RutasApi.V1}/auth";

    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 3, "Ana", "Pérez", "ana.perez@demo-a.co", "hash");

    private (HttpClient Cliente, TokenEmitido Token) Cliente(bool conToken = true)
    {
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s.AddScoped(_ => _sesiones)))
            .CreateClient();

        var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
        var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, "AC-03");
        if (conToken)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        return (cliente, token);
    }

    private SesionToken Registrar(TokenEmitido token)
    {
        var sesion = new SesionToken(_usuario.TenantId, _usuario.Id, token.Jti, token.Emision, token.Expiracion);
        _sesiones.EstaActivaAsync(_usuario.TenantId, token.Jti, Arg.Any<CancellationToken>()).Returns(_ => !sesion.EstaRevocada);
        _sesiones.ObtenerAsync(token.Jti, Arg.Any<CancellationToken>()).Returns(sesion);
        _sesiones.BuscarUsuarioAsync(_usuario.Id, Arg.Any<CancellationToken>()).Returns(new UsuarioParaAcceso(_usuario, "AC-03", true));
        return sesion;
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task SinToken_Responde401(string ruta)
    {
        var (cliente, _) = Cliente(conToken: false);

        var respuesta = await cliente.PostAsync($"{Auth}/{ruta}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CA_HU_012_02_Logout_Responde204YRevocaElJti()
    {
        var (cliente, token) = Cliente();
        var sesion = Registrar(token);

        var respuesta = await cliente.PostAsync($"{Auth}/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(MotivosRevocacion.CierreDeSesion, sesion.MotivoRevocacion);
    }

    [Fact]
    public async Task CA_HU_012_02_TrasElLogout_ElMismoTokenSeRechazaEnCualquierRuta()
    {
        var (cliente, token) = Cliente();
        Registrar(token);
        await cliente.PostAsync($"{Auth}/logout", null);

        var renovar = await cliente.PostAsync($"{Auth}/refresh", null);
        var otraVez = await cliente.PostAsync($"{Auth}/logout", null);
        var usuarios = await cliente.GetAsync($"{RutasApi.V1}/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otraVez.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, usuarios.StatusCode);
    }

    [Fact]
    public async Task CA_HU_012_01_LogoutPorInactividad_GuardaEseMotivo()
    {
        var (cliente, token) = Cliente();
        var sesion = Registrar(token);

        var respuesta = await cliente.PostAsJsonAsync($"{Auth}/logout", new { motivo = "inactividad" });

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(MotivosRevocacion.Inactividad, sesion.MotivoRevocacion);
    }

    [Fact]
    public async Task Logout_MotivoDesconocido_Responde400()
    {
        var (cliente, token) = Cliente();
        var sesion = Registrar(token);

        var respuesta = await cliente.PostAsJsonAsync($"{Auth}/logout", new { motivo = "otro" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.False(sesion.EstaRevocada);
    }

    [Fact]
    public async Task Refresh_DevuelveOtroTokenYRevocaElAnterior()
    {
        var (cliente, token) = Cliente();
        var anterior = Registrar(token);

        var respuesta = await cliente.PostAsync($"{Auth}/refresh", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.NotEqual(token.Token, cuerpo.RootElement.GetProperty("token").GetString());
        Assert.Equal("AC-03", cuerpo.RootElement.GetProperty("rol").GetString());
        Assert.Equal(MotivosRevocacion.Renovacion, anterior.MotivoRevocacion);
        _sesiones.Received(1).Agregar(Arg.Is<SesionToken>(s => s.Jti != token.Jti && s.UsuarioId == _usuario.Id));
    }

    [Fact]
    public async Task TokenSinSesionRegistrada_Responde401()
    {
        var (cliente, _) = Cliente();

        var respuesta = await cliente.PostAsync($"{Auth}/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }
}
