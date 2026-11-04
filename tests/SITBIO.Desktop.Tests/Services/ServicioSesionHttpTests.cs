using System.Net;
using System.Net.Http;
using System.Text;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioSesionHttpTests
{
    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? Ruta { get; private set; }

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ruta = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            Cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private static (ServicioSesionHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioSesionHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task Renovar_200_DevuelveLaSesionNueva()
    {
        var (servicio, manejador) = Crear(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"token":"nuevo","expiraEn":"2026-11-03T15:00:00+00:00","rol":"AC-02","debeCambiarClave":false}""", Encoding.UTF8, "application/json"),
        });

        var resultado = await servicio.RenovarAsync(CancellationToken.None);

        Assert.Equal("POST /api/v1/auth/refresh", manejador.Ruta);
        Assert.Equal("nuevo", resultado.Sesion?.Token);
        Assert.False(resultado.Rechazada);
    }

    [Fact]
    public async Task Renovar_401_EsRechazo()
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var resultado = await servicio.RenovarAsync(CancellationToken.None);

        Assert.Null(resultado.Sesion);
        Assert.True(resultado.Rechazada);
    }

    [Fact]
    public async Task Renovar_SinServidor_NoEsRechazo()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("sin red"));

        var resultado = await servicio.RenovarAsync(CancellationToken.None);

        Assert.Same(ResultadoRenovacion.SinConexion, resultado);
    }

    [Fact]
    public async Task Cerrar_EnviaElMotivo()
    {
        var (servicio, manejador) = Crear(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await servicio.CerrarAsync(ServicioSesionHttp.MotivoInactividad, CancellationToken.None);

        Assert.Equal("POST /api/v1/auth/logout", manejador.Ruta);
        Assert.Contains("\"motivo\":\"inactividad\"", manejador.Cuerpo);
    }

    [Fact]
    public async Task Cerrar_SinServidor_NoFalla()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("sin red"));

        await servicio.CerrarAsync(ServicioSesionHttp.MotivoVoluntario, CancellationToken.None);
    }
}
