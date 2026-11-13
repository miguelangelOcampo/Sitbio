using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-PLAN y API-ALERTAS desde el cliente (HU-019, HU-020): rutas, cuerpos, lectura y errores.</summary>
public class ServicioMantenimientoHttpTests
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

    private static readonly Guid Equipo = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly string PlanJson =
        $$"""{"id":"{{PlanId}}","equipoId":"{{Equipo}}","placaInventario":"PL-001","tipoEquipo":"Bomba","ubicacion":"UCI","descripcion":"Mantenimiento preventivo","periodicidadDias":30,"ultimaEjecucion":"2026-10-27","proximaFecha":"2026-11-26","estado":"activo","vencido":false,"fechaCreacion":"2026-11-06T15:00:00+00:00"}""";

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioMantenimientoHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioMantenimientoHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task Listar_ArmaLaConsulta_YLeeLosPlanes()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""{"items":[{{PlanJson}}],"page":1,"pageSize":20,"total":1}"""));

        var pagina = await servicio.ListarPlanesAsync(new FiltroPlanes(Equipo, EstadosPlan.Activo, 2), CancellationToken.None);

        Assert.Equal($"GET /api/v1/planes-preventivos?page=2&pageSize=20&equipoId={Equipo}&estado=activo", Assert.Single(manejador.Rutas));
        var plan = Assert.Single(pagina.Items);
        Assert.Equal((PlanId, "PL-001", 30, new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 26)), (plan.Id, plan.PlacaInventario, plan.PeriodicidadDias, plan.UltimaEjecucion!.Value, plan.ProximaFecha));
    }

    [Fact]
    public async Task Crear_YActualizar_EnvianSoloLoQueCambia()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, PlanJson));

        await servicio.CrearPlanAsync(new DatosPlan(Equipo, null, 30, new DateOnly(2026, 10, 27), null), CancellationToken.None);
        Assert.Equal("POST /api/v1/planes-preventivos", manejador.Rutas[^1]);
        using (var cuerpo = JsonDocument.Parse(manejador.Cuerpo!))
        {
            Assert.Equal((30, "2026-10-27", JsonValueKind.Null), (cuerpo.RootElement.GetProperty("periodicidadDias").GetInt32(), cuerpo.RootElement.GetProperty("ultimaEjecucion").GetString(), cuerpo.RootElement.GetProperty("proximaFecha").ValueKind));
        }

        await servicio.ActualizarPlanAsync(PlanId, new DatosActualizacionPlan(Estado: EstadosPlan.Suspendido), CancellationToken.None);
        Assert.Equal($"PUT /api/v1/planes-preventivos/{PlanId}", manejador.Rutas[^1]);
        using var actualizacion = JsonDocument.Parse(manejador.Cuerpo!);
        Assert.Equal(("suspendido", JsonValueKind.Null), (actualizacion.RootElement.GetProperty("estado").GetString(), actualizacion.RootElement.GetProperty("periodicidadDias").ValueKind));
    }

    [Fact]
    public async Task Alertas_LeeLosDiasLasAlertasYLasOrdenesGeneradas()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""
            {"diasAnticipacion":20,"ordenesGeneradas":1,"alertas":[
              {"tipo":"preventivo","estado":"vencida","fecha":"2026-11-02","diasRestantes":-4,"equipoId":"{{Equipo}}","placaInventario":"PL-001","tipoEquipo":"Bomba","ubicacion":"UCI","descripcion":"Preventivo","planId":"{{PlanId}}","ordenId":"{{Guid.Empty}}","estadoOrden":"asignada"}]}
            """));

        var resumen = await servicio.AlertasAsync(CancellationToken.None);

        Assert.Equal("GET /api/v1/alertas", Assert.Single(manejador.Rutas));
        Assert.Equal((20, 1), (resumen.DiasAnticipacion, resumen.OrdenesGeneradas));
        var alerta = Assert.Single(resumen.Alertas);
        Assert.Equal((TiposAlerta.Vencida, -4, (Guid?)PlanId, "asignada"), (alerta.Estado, alerta.DiasRestantes, alerta.PlanId, alerta.EstadoOrden));
        Assert.Equal($"preventivo:{PlanId}:2026-11-02", alerta.Clave);
    }

    [Fact]
    public async Task Errores_ConMensajeEnEspanol_YElCodigoDeLaApi()
    {
        var (prohibido, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}"""));
        var error = await Assert.ThrowsAsync<ErrorApi>(() => prohibido.CrearPlanAsync(new DatosPlan(Equipo, null, 30, null, null), CancellationToken.None));
        Assert.Equal("Solo el Ingeniero Biomédico crea y modifica los planes de mantenimiento.", error.Message);

        var (conflicto, _) = Crear(_ => Json(HttpStatusCode.Conflict, """{"title":"Conflicto","status":409,"detail":"El equipo está dado de baja.","codigo":"PLN-001"}"""));
        var baja = await Assert.ThrowsAsync<ErrorApi>(() => conflicto.CrearPlanAsync(new DatosPlan(Equipo, null, 30, null, null), CancellationToken.None));
        Assert.Equal(("PLN-001", "El equipo está dado de baja."), (baja.Codigo, baja.Message));

        var (sinRol, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}"""));
        Assert.Equal("Tu rol no recibe alertas de vencimiento.", (await Assert.ThrowsAsync<ErrorApi>(() => sinRol.AlertasAsync(CancellationToken.None))).Message);
    }
}
