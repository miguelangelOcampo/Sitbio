using System.Net;
using System.Net.Http;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ManejadorTokenTests
{
    private sealed class Destino(HttpStatusCode estado = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Recibida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibida = request;
            return Task.FromResult(new HttpResponseMessage(estado));
        }
    }

    private static async Task<HttpRequestMessage> Enviar(SesionActual sesion, HttpStatusCode estado = HttpStatusCode.OK)
    {
        var destino = new Destino(estado);
        var cliente = new HttpClient(new ManejadorToken(sesion) { InnerHandler = destino }) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        await cliente.GetAsync("equipos");
        return destino.Recibida!;
    }

    [Fact]
    public async Task ConSesion_AdjuntaElTokenBearer()
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.UtcNow.AddMinutes(30), "AC-02", false));

        var solicitud = await Enviar(sesion);

        Assert.Equal("Bearer", solicitud.Headers.Authorization?.Scheme);
        Assert.Equal("a.b.c", solicitud.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SinSesion_NoAdjuntaNada()
    {
        var solicitud = await Enviar(new SesionActual());

        Assert.Null(solicitud.Headers.Authorization);
    }

    [Fact]
    public async Task SesionCerrada_DejaDeAdjuntarElToken()
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.UtcNow.AddMinutes(30), "AC-02", false));
        sesion.Cerrar();

        var solicitud = await Enviar(sesion);

        Assert.Null(solicitud.Headers.Authorization);
    }

    [Fact]
    public async Task ConSesion_Un401DeLaApiAvisaElRechazo()
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.UtcNow.AddMinutes(30), "AC-02", false));
        var avisos = 0;
        sesion.Rechazada += (_, _) => avisos++;

        await Enviar(sesion, HttpStatusCode.Unauthorized);
        await Enviar(sesion, HttpStatusCode.Forbidden);

        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task SinSesion_Un401NoAvisaNada()
    {
        var sesion = new SesionActual();
        var avisos = 0;
        sesion.Rechazada += (_, _) => avisos++;

        await Enviar(sesion, HttpStatusCode.Unauthorized);

        Assert.Equal(0, avisos);
    }
}
