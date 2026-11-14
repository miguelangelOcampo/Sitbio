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

    [Fact]
    public async Task Calibraciones_ArmanLasRutas_YLeenLaCalibracion()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var calibracion = $$"""{"id":"{{id}}","equipoId":"{{Equipo}}","placaInventario":"MON-07","tipoEquipo":"Monitor","ubicacion":"UCI","proveedorId":null,"proveedor":null,"fechaCalibracion":"2026-11-04","patronReferencia":"Analizador","resultado":"conforme","numeroCertificado":null,"fechaVencimiento":"2027-11-04","vigente":true,"registradoPorId":"{{Guid.Empty}}","registradoPor":"Ana Mora","aprobadoPorId":null,"aprobadoPor":null,"aprobada":false}""";
        var (servicio, manejador) = Crear(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, $$"""{"items":[{{calibracion}}],"page":1,"pageSize":20,"total":1}""")
            : Json(HttpStatusCode.OK, calibracion));

        var pagina = await servicio.ListarCalibracionesAsync(new FiltroCalibraciones(Equipo), CancellationToken.None);
        Assert.Equal($"GET /api/v1/calibraciones?page=1&pageSize=20&equipoId={Equipo}", manejador.Rutas[^1]);
        Assert.Equal(("MON-07", new DateOnly(2027, 11, 4), true), (pagina.Items[0].PlacaInventario, pagina.Items[0].FechaVencimiento, pagina.Items[0].Vigente));

        await servicio.RegistrarCalibracionAsync(new DatosCalibracion(Equipo, new DateOnly(2026, 11, 4), "Analizador", ResultadosCalibracion.Conforme, null, null), CancellationToken.None);
        Assert.Equal("POST /api/v1/calibraciones", manejador.Rutas[^1]);
        using (var cuerpo = JsonDocument.Parse(manejador.Cuerpo!))
        {
            Assert.Equal(("2026-11-04", "conforme", JsonValueKind.Null), (cuerpo.RootElement.GetProperty("fechaCalibracion").GetString(), cuerpo.RootElement.GetProperty("resultado").GetString(), cuerpo.RootElement.GetProperty("fechaVencimiento").ValueKind));
        }

        await servicio.CorregirCalibracionAsync(id, new DatosCorreccionCalibracion(NumeroCertificado: "CERT-1"), CancellationToken.None);
        Assert.Equal($"PATCH /api/v1/calibraciones/{id}", manejador.Rutas[^1]);
        await servicio.AprobarCalibracionAsync(id, CancellationToken.None);
        Assert.Equal(($"PATCH /api/v1/calibraciones/{id}/aprobacion", (string?)null), (manejador.Rutas[^1], manejador.Cuerpo));
    }

    [Fact]
    public async Task Calibraciones_Errores_ConMensajeEnEspanol()
    {
        var (prohibido, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}"""));
        Assert.Equal(
            "Solo el Técnico de Mantenimiento registra y corrige las calibraciones.",
            (await Assert.ThrowsAsync<ErrorApi>(() => prohibido.RegistrarCalibracionAsync(new DatosCalibracion(Equipo, new DateOnly(2026, 11, 4), "Patrón", ResultadosCalibracion.Conforme, null, null), CancellationToken.None))).Message);
        Assert.Equal("Solo el Ingeniero Biomédico aprueba las calibraciones.", (await Assert.ThrowsAsync<ErrorApi>(() => prohibido.AprobarCalibracionAsync(Guid.NewGuid(), CancellationToken.None))).Message);

        var (conflicto, _) = Crear(_ => Json(HttpStatusCode.Conflict, """{"title":"Conflicto","status":409,"detail":"El equipo EST-01 no requiere calibración (RN-010).","codigo":"CAL-001"}"""));
        var error = await Assert.ThrowsAsync<ErrorApi>(() => conflicto.RegistrarCalibracionAsync(new DatosCalibracion(Equipo, new DateOnly(2026, 11, 4), "Patrón", ResultadosCalibracion.Conforme, null, null), CancellationToken.None));
        Assert.Equal(("CAL-001", "El equipo EST-01 no requiere calibración (RN-010)."), (error.Codigo, error.Message));
    }
}
