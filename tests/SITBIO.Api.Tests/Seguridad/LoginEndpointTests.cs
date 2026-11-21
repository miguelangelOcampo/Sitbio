using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Api.Modules.Autenticacion;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>Contrato HTTP de POST /api/v1/auth/login, sin base de datos (repositorio simulado).</summary>
public class LoginEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Clave = "Clave-Correcta-2026";
    private static readonly string Hash = new HasherBCrypt().Hash(Clave);
    private static readonly string Ruta = $"{RutasApi.V1}/auth/login";

    private HttpClient Cliente(int intentosPorMinuto = 50, bool detrasDelTunel = false)
    {
        var usuario = new Usuario(Guid.NewGuid(), 2, "Iván", "Gómez", "ivan.gomez@demo-a.co", Hash);
        var repositorio = Substitute.For<IRepositorioAcceso>();
        repositorio.BuscarPorCorreoAsync("ivan.gomez@demo-a.co", Arg.Any<CancellationToken>())
            .Returns(new UsuarioParaAcceso(usuario, CodigosRol.IngenieroBiomedico, true));

        return fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", "clave-de-pruebas-con-mas-de-32-bytes!!")
                .UseSetting(AutenticacionEndpoints.SeccionLimite, intentosPorMinuto.ToString())
                .ConfigureTestServices(s =>
                {
                    s.AddScoped(_ => repositorio).AddScoped(_ => Substitute.For<IBitacoraAuditoria>());
                    if (detrasDelTunel)
                    {
                        s.AddSingleton<IStartupFilter, DesdeElTunelLocal>();
                    }
                }))
            .CreateClient();
    }

    /// <summary>Como con cloudflared: toda petición llega desde 127.0.0.1 y trae la IP real en X-Forwarded-For.</summary>
    private sealed class DesdeElTunelLocal : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
        {
            app.Use((contexto, continuar) =>
            {
                contexto.Connection.RemoteIpAddress = IPAddress.Loopback;
                return continuar(contexto);
            });
            siguiente(app);
        };
    }

    private static Task<HttpResponseMessage> Login(HttpClient cliente, string correo, string clave) =>
        cliente.PostAsJsonAsync(Ruta, new { correo, contrasena = clave });

    [Fact]
    public async Task CredencialesValidas_Responde200ConTokenYRol()
    {
        var respuesta = await Login(Cliente(), "ivan.gomez@demo-a.co", Clave);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(3, cuerpo.RootElement.GetProperty("token").GetString()!.Split('.').Length);
        Assert.Equal("AC-02", cuerpo.RootElement.GetProperty("rol").GetString());
        Assert.False(cuerpo.RootElement.GetProperty("debeCambiarClave").GetBoolean());
        Assert.True(cuerpo.RootElement.TryGetProperty("expiraEn", out _));
    }

    [Theory]
    [InlineData("ivan.gomez@demo-a.co", "otra-clave")]
    [InlineData("nadie@demo-a.co", Clave)]
    public async Task CredencialesInvalidas_Responde401ConMsg001(string correo, string clave)
    {
        var respuesta = await Login(Cliente(), correo, clave);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("MSG-001", cuerpo.RootElement.GetProperty("codigo").GetString());
        Assert.Equal(IniciarSesion.MensajeCredenciales, cuerpo.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MasIntentosQueElLimite_Responde429()
    {
        var cliente = Cliente(intentosPorMinuto: 2);

        await Login(cliente, "nadie@demo-a.co", "x");
        await Login(cliente, "nadie@demo-a.co", "x");
        var tercera = await Login(cliente, "nadie@demo-a.co", "x");

        Assert.Equal(HttpStatusCode.TooManyRequests, tercera.StatusCode);
        Assert.Equal("application/problem+json", tercera.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DetrasDelTunel_ElLimiteEsPorUsuarioYNoCompartido()
    {
        var cliente = Cliente(intentosPorMinuto: 2, detrasDelTunel: true);

        Task<HttpResponseMessage> Desde(string ip)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, Ruta) { Content = JsonContent.Create(new { correo = "nadie@demo-a.co", contrasena = "x" }) };
            peticion.Headers.Add("X-Forwarded-For", ip);
            return cliente.SendAsync(peticion);
        }

        await Desde("203.0.113.10");
        await Desde("203.0.113.10");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Desde("203.0.113.10")).StatusCode);

        // Otro usuario detrás del mismo túnel no queda bloqueado por los intentos del primero.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Desde("198.51.100.20")).StatusCode);
    }

    [Fact]
    public async Task DatosVacios_Responde400()
    {
        var respuesta = await Login(Cliente(), "", "");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }
}
