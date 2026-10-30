using System.Net;
using System.Net.Http;
using System.Text;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioAutenticacionHttpTests
{
    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Recibida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibida = request;
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioAutenticacionHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioAutenticacionHttp(http), manejador);
    }

    [Fact]
    public async Task Respuesta200_DevuelveLaSesion()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK,
            """{"token":"a.b.c","expiraEn":"2026-10-30T15:30:00+00:00","rol":"AC-02","debeCambiarClave":true}"""));

        var resultado = await servicio.IniciarSesionAsync("ana@demo.co", "clave", CancellationToken.None);

        Assert.Equal("a.b.c", resultado.Sesion?.Token);
        Assert.Equal("AC-02", resultado.Sesion?.Rol);
        Assert.True(resultado.Sesion?.DebeCambiarClave);
        Assert.Equal("http://localhost:5080/api/v1/auth/login", manejador.Recibida?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, manejador.Recibida?.Method);
    }

    [Theory]
    [InlineData("MSG-001", FalloLogin.CredencialesIncorrectas)]
    [InlineData("MSG-002", FalloLogin.AccesoNoDisponible)]
    public async Task Respuesta401_SeTraducePorElCodigo(string codigo, FalloLogin esperado)
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Unauthorized, $$"""{"status":401,"codigo":"{{codigo}}"}"""));

        var resultado = await servicio.IniciarSesionAsync("ana@demo.co", "clave", CancellationToken.None);

        Assert.Equal(esperado, resultado.Fallo);
    }

    [Fact]
    public async Task Respuesta429_EsDemasiadosIntentos()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.TooManyRequests, "{}"));

        var resultado = await servicio.IniciarSesionAsync("ana@demo.co", "clave", CancellationToken.None);

        Assert.Equal(FalloLogin.DemasiadosIntentos, resultado.Fallo);
    }

    [Fact]
    public async Task SinServidor_EsSinConexion()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("sin conexión"));

        var resultado = await servicio.IniciarSesionAsync("ana@demo.co", "clave", CancellationToken.None);

        Assert.Equal(FalloLogin.SinConexion, resultado.Fallo);
    }

    [Fact]
    public async Task Respuesta500_EsInesperado()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.InternalServerError, "{}"));

        var resultado = await servicio.IniciarSesionAsync("ana@demo.co", "clave", CancellationToken.None);

        Assert.Equal(FalloLogin.Inesperado, resultado.Fallo);
    }
}
