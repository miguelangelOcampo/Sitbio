using System.Net;
using System.Net.Http;
using System.Text;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioBitacoraHttpTests
{
    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Rutas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Rutas.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioBitacoraHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioBitacoraHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task SinFiltros_SoloEnviaLaPagina()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":50,"total":0}"""));

        await servicio.ConsultarAsync(new FiltroConsultaBitacora(), CancellationToken.None);

        Assert.Equal("GET /api/v1/bitacora?page=1&pageSize=50", manejador.Rutas.Single());
    }

    [Fact]
    public async Task ConFiltros_EnviaUsuarioFechasYAccion()
    {
        var usuario = Guid.NewGuid();
        var desde = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, """{"items":[],"page":2,"pageSize":50,"total":0}"""));

        await servicio.ConsultarAsync(new FiltroConsultaBitacora(usuario, desde, desde.AddDays(5), "LOGIN_FALLIDO", 2, 50), CancellationToken.None);

        Assert.Equal(
            $"GET /api/v1/bitacora?usuarioId={usuario}&desde=2026-11-01T00%3A00%3A00.0000000-05%3A00&hasta=2026-11-06T00%3A00%3A00.0000000-05%3A00&accion=LOGIN_FALLIDO&page=2&pageSize=50",
            manejador.Rutas.Single());
    }

    [Fact]
    public async Task LeeLosEventosConSuDetalle()
    {
        var usuario = Guid.NewGuid();
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.OK, $$"""
            {"items":[{"id":"{{Guid.NewGuid()}}","fechaHora":"2026-11-05T15:30:00+00:00","usuarioId":"{{usuario}}","usuario":"Laura Gómez",
              "accion":"ACCESO_DENEGADO","entidad":"usuarios","entidadId":null,"ipOrigen":"10.0.0.5",
              "detalle":{"metodo":"POST","ruta":"/api/v1/usuarios","rol":"AC-02"} }],
             "page":1,"pageSize":50,"total":61}
            """));

        var pagina = await servicio.ConsultarAsync(new FiltroConsultaBitacora(), CancellationToken.None);

        Assert.Equal(61, pagina.Total);
        var evento = Assert.Single(pagina.Items);
        Assert.Equal(usuario, evento.UsuarioId);
        Assert.Equal("Laura Gómez", evento.Usuario);
        Assert.Equal("AC-02", evento.Detalle!["rol"]);
    }

    [Fact]
    public async Task Prohibido_DaUnMensajeClaro()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"title":"Prohibido","status":403}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ConsultarAsync(new FiltroConsultaBitacora(), CancellationToken.None));

        Assert.Equal("Tu rol no permite consultar la bitácora de auditoría.", error.Message);
    }

    [Fact]
    public async Task Validacion_MuestraElErrorDelCampo()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"Datos no válidos","status":400,"errors":{"hasta":["La fecha final debe ser posterior a la inicial."]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ConsultarAsync(new FiltroConsultaBitacora(), CancellationToken.None));

        Assert.Equal("La fecha final debe ser posterior a la inicial.", error.Message);
    }

    [Fact]
    public async Task SinServidor_EsMsg011()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("rechazada"));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ConsultarAsync(new FiltroConsultaBitacora(), CancellationToken.None));

        Assert.True(error.SinConexion);
    }
}
