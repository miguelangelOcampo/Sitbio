using System.Net;
using System.Net.Http;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ManejadorTokenTests
{
    private sealed class Destino : HttpMessageHandler
    {
        public HttpRequestMessage? Recibida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibida = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> Enviar(SesionActual sesion)
    {
        var destino = new Destino();
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
}
