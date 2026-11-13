using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-OT desde el cliente (HU-004): técnicos con su carga, cuerpo de la asignación y errores.</summary>
public class ServicioOrdenesHttpTests
{
    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Rutas { get; } = [];

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Rutas.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            Cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private static readonly Guid Tiquete = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tecnico = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioOrdenesHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioOrdenesHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task Tecnicos_LeeLosActivosConSuCarga()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""[{"id":"{{Tecnico}}","nombre":"Carlos Ruiz","ordenesAbiertas":2}]"""));

        var tecnicos = await servicio.TecnicosAsync(CancellationToken.None);

        Assert.Equal("GET /api/v1/ordenes/tecnicos", manejador.Rutas.Single());
        Assert.Equal([new TecnicoAsignable(Tecnico, "Carlos Ruiz", 2)], tecnicos);
    }

    [Fact]
    public async Task Asignar_EnviaTiqueteTecnicoPrioridadYFecha_YLeeLaOrden()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.Created, $$"""
            {"id":"{{Guid.NewGuid()}}","equipoId":"{{Guid.NewGuid()}}","placaInventario":"HSV-EQ-0412","tiqueteId":"{{Tiquete}}","codigoTiquete":"TQT-000003",
             "tecnicoId":"{{Tecnico}}","tecnico":"Carlos Ruiz","tipoMantenimiento":"correctivo","prioridad":"alta","estado":"asignada",
             "fechaProgramada":"2026-11-14","fechaAsignacion":"2026-11-13T15:00:00+00:00"}
            """));

        var orden = await servicio.AsignarAsync(new DatosOrden(Tiquete, Tecnico, "alta", new DateOnly(2026, 11, 14)), CancellationToken.None);

        Assert.Equal("POST /api/v1/ordenes", manejador.Rutas.Single());
        using var cuerpo = JsonDocument.Parse(manejador.Cuerpo!);
        Assert.Equal(
            (Tiquete, Tecnico, "alta", "2026-11-14"),
            (cuerpo.RootElement.GetProperty("tiqueteId").GetGuid(), cuerpo.RootElement.GetProperty("tecnicoId").GetGuid(),
             cuerpo.RootElement.GetProperty("prioridad").GetString(), cuerpo.RootElement.GetProperty("fechaProgramada").GetString()));
        Assert.Equal(("TQT-000003", "Carlos Ruiz", "asignada", (DateOnly?)new DateOnly(2026, 11, 14)), (orden.CodigoTiquete, orden.Tecnico, orden.Estado, orden.FechaProgramada));
    }

    [Fact]
    public async Task Asignar_Rn003_TraeElErrorDelCampoTecnico()
    {
        const string Rn003 = "Solo puedes asignar la orden a un Técnico de Mantenimiento activo de tu institución (RN-003).";
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest, $$$"""{"status":400,"errors":{"tecnicoId":["{{{Rn003}}}"]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AsignarAsync(new DatosOrden(Tiquete, Tecnico, null, null), CancellationToken.None));

        Assert.Equal(Rn003, error.Errores["tecnicoId"]);
    }

    [Fact]
    public async Task Asignar_Conflicto_TraeElCodigoYElMensajeDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict, """{"status":409,"detail":"El tiquete TQT-000003 ya tiene una orden de trabajo.","codigo":"OT-001"}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AsignarAsync(new DatosOrden(Tiquete, Tecnico, null, null), CancellationToken.None));

        Assert.Equal(("OT-001", "El tiquete TQT-000003 ya tiene una orden de trabajo."), (error.Codigo, error.Message));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Tu rol no permite asignar órdenes de trabajo.")]
    [InlineData(HttpStatusCode.NotFound, "Ese tiquete ya no está en la bandeja de tu institución. Actualiza la lista.")]
    public async Task Asignar_PermisoOTiqueteAjeno_DaUnMensajeClaro(HttpStatusCode estado, string mensaje)
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(estado));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AsignarAsync(new DatosOrden(Tiquete, Tecnico, null, null), CancellationToken.None));

        Assert.Equal(mensaje, error.Message);
    }
}
