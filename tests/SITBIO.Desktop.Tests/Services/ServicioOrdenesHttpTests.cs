using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-OT desde el cliente (HU-004, HU-005, HU-021): técnicos con su carga, asignación, ejecución, cierre, aprobación y errores.</summary>
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

    // ---------- HU-005: ejecución e intervención ----------

    private const string OrdenJson = """
        {"id":"33333333-3333-3333-3333-333333333333","equipoId":"44444444-4444-4444-4444-444444444444","placaInventario":"BOM-05",
         "tipoEquipo":"Bomba de infusión","ubicacion":"UCI","tiqueteId":null,"codigoTiquete":"TQT-000007","tipoFalla":"No enciende",
         "tecnicoId":"22222222-2222-2222-2222-222222222222","tecnico":"Carlos Ruiz","tipoMantenimiento":"correctivo","prioridad":"alta",
         "estado":"en_ejecucion","fechaProgramada":null,"fechaInicio":"2026-11-13T15:00:00+00:00","tieneIntervencion":true}
        """;

    private static readonly Guid OrdenId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Listar_PideLasDelTecnicoConFiltroYPagina_YLeeLosDatosDeLaOrden()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""{"items":[{{OrdenJson}}],"page":2,"pageSize":20,"total":21}"""));

        var pagina = await servicio.ListarAsync(new FiltroOrdenes("en_ejecucion", true, 2, 20), CancellationToken.None);

        Assert.Equal("GET /api/v1/ordenes?page=2&pageSize=20&estado=en_ejecucion&asignadas=me", manejador.Rutas.Single());
        var orden = Assert.Single(pagina.Items);
        Assert.Equal(("BOM-05", "Bomba de infusión", "UCI", "No enciende", true), (orden.PlacaInventario, orden.TipoEquipo, orden.Ubicacion, orden.TipoFalla, orden.TieneIntervencion));
        Assert.Equal((Guid.Parse("44444444-4444-4444-4444-444444444444"), 21), (orden.EquipoId, pagina.Total));
    }

    [Fact]
    public async Task CambiarEstado_EnviaLaAccion()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, OrdenJson));

        var orden = await servicio.CambiarEstadoAsync(OrdenId, AccionesOrden.Iniciar, CancellationToken.None);

        Assert.Equal($"PATCH /api/v1/ordenes/{OrdenId}/estado", manejador.Rutas.Single());
        Assert.Equal("""{"accion":"iniciar"}""", manejador.Cuerpo);
        Assert.Equal(EstadosOrden.EnEjecucion, orden.Estado);
    }

    [Fact]
    public async Task ObtenerIntervencion_SinRegistrar_DevuelveNull_YConRegistroLaLee()
    {
        var (sinRegistro, _) = Crear(_ => Json(HttpStatusCode.NotFound, """{"status":404,"detail":"La intervención no existe."}"""));
        Assert.Null(await sinRegistro.ObtenerIntervencionAsync(OrdenId, CancellationToken.None));

        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""
            {"id":"{{Guid.NewGuid()}}","ordenId":"{{OrdenId}}","diagnostico":"D","accionesRealizadas":"A","repuestos":[{"nombre":"Sensor","cantidad":2,"costoUnitario":15000}],
             "costoManoObra":null,"costoTotal":30000,"tiempoAtencionMin":null,"estadoEquipoSalida":null,
             "camposDinamicos":{"tasa_goteo":25.5,"vence":"2027-05-01"},"versionCampos":2,"fechaIntervencion":"2026-11-13T15:00:00+00:00","cerradaFirmada":false}
            """));
        var intervencion = (await servicio.ObtenerIntervencionAsync(OrdenId, CancellationToken.None))!;

        Assert.Equal($"GET /api/v1/ordenes/{OrdenId}/intervencion", manejador.Rutas.Single());
        Assert.Equal((30000m, (int?)2), (intervencion.CostoTotal, intervencion.VersionCampos));
        Assert.Equal(25.5, intervencion.CamposDinamicos["tasa_goteo"].GetDouble());
        Assert.Equal([new Repuesto("Sensor", 2, 15000m)], intervencion.Repuestos);
    }

    [Fact]
    public async Task Registrar_EnviaLosCamposDinamicosConSuTipoJson()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""
            {"id":"{{Guid.NewGuid()}}","ordenId":"{{OrdenId}}","diagnostico":"D","accionesRealizadas":"A","repuestos":[],"costoTotal":0,
             "camposDinamicos":{},"fechaIntervencion":"2026-11-13T15:00:00+00:00","cerradaFirmada":false}
            """));
        var datos = new DatosIntervencion("D", "A", [], null, 90, "Operativo",
            new Dictionary<string, object> { ["tasa_goteo"] = 25.5m, ["ciclos"] = 3L, ["calibrado"] = true, ["vence"] = "2027-05-01" });

        await servicio.RegistrarIntervencionAsync(OrdenId, datos, CancellationToken.None);

        Assert.Equal($"PUT /api/v1/ordenes/{OrdenId}/intervencion", manejador.Rutas.Single());
        using var cuerpo = JsonDocument.Parse(manejador.Cuerpo!);
        var campos = cuerpo.RootElement.GetProperty("camposDinamicos");
        Assert.Equal((JsonValueKind.Number, JsonValueKind.Number, JsonValueKind.True, JsonValueKind.String),
            (campos.GetProperty("tasa_goteo").ValueKind, campos.GetProperty("ciclos").ValueKind, campos.GetProperty("calibrado").ValueKind, campos.GetProperty("vence").ValueKind));
        Assert.Equal(90, cuerpo.RootElement.GetProperty("tiempoAtencionMin").GetInt32());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "No se pudo guardar la intervención porque el servidor de documentos no respondió. La orden no cambió; intenta de nuevo.")]
    [InlineData(HttpStatusCode.Forbidden, "Solo el técnico de mantenimiento ejecuta las órdenes de trabajo.")]
    [InlineData(HttpStatusCode.NotFound, "Esta orden ya no está asignada a ti. Actualiza la lista.")]
    public async Task Registrar_ErroresSinDetalle_DanUnMensajeClaro(HttpStatusCode estado, string mensaje)
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(estado));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.RegistrarIntervencionAsync(OrdenId, new DatosIntervencion("D", "A", [], null, null, null, new Dictionary<string, object>()), CancellationToken.None));

        Assert.Equal(mensaje, error.Message);
    }

    [Fact]
    public async Task Registrar_Rn004_TraeElCodigo()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict, """{"status":409,"detail":"La intervención ya está firmada y aprobada; no se puede modificar (RN-004).","codigo":"RN-004"}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.RegistrarIntervencionAsync(OrdenId, new DatosIntervencion("D", "A", [], null, null, null, new Dictionary<string, object>()), CancellationToken.None));

        Assert.Equal("RN-004", error.Codigo);
    }

    // ---------- HU-021: cierre y aprobación ----------

    [Fact]
    public async Task CambiarEstado_Cerrar_EnviaLaAccion()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, OrdenJson.Replace("\"en_ejecucion\"", "\"cerrada\"", StringComparison.Ordinal)));

        var orden = await servicio.CambiarEstadoAsync(OrdenId, AccionesOrden.Cerrar, CancellationToken.None);

        Assert.Equal("""{"accion":"cerrar"}""", manejador.Cuerpo);
        Assert.Equal(EstadosOrden.Cerrada, orden.Estado);
    }

    [Fact]
    public async Task Aprobar_EnviaLaFirma_YLeeLaOrdenAprobada()
    {
        var aprobada = OrdenJson.Replace("\"en_ejecucion\"", "\"aprobada\"", StringComparison.Ordinal).Replace(
            "\"tieneIntervencion\":true", "\"tieneIntervencion\":true,\"fechaCierre\":\"2026-11-13T16:00:00+00:00\",\"aprobadoPorId\":\"55555555-5555-5555-5555-555555555555\",\"fechaAprobacion\":\"2026-11-13T17:00:00+00:00\",\"firmaAprobacion\":\"Laura Gómez\"",
            StringComparison.Ordinal);
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, aprobada));

        var orden = await servicio.AprobarAsync(OrdenId, "Laura Gómez", CancellationToken.None);

        Assert.Equal($"POST /api/v1/ordenes/{OrdenId}/aprobacion", manejador.Rutas.Single());
        Assert.Equal("Laura Gómez", JsonDocument.Parse(manejador.Cuerpo!).RootElement.GetProperty("firma").GetString());
        Assert.Equal((EstadosOrden.Aprobada, "Laura Gómez", (Guid?)Guid.Parse("55555555-5555-5555-5555-555555555555")), (orden.Estado, orden.FirmaAprobacion, orden.AprobadoPorId));
        Assert.NotNull(orden.FechaCierre);
        Assert.NotNull(orden.FechaAprobacion);
    }

    [Fact]
    public async Task Aprobar_CalibracionVencida_TraeElCodigoYElMensajeDeLaApi()
    {
        const string Msg013 = "La calibración de este equipo venció el 01/10/2026. Registre una nueva calibración antes de cerrar la orden como «Operativo».";
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict, $$$"""{"status":409,"detail":"{{{Msg013}}}","codigo":"RN-007"}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AprobarAsync(OrdenId, "Laura Gómez", CancellationToken.None));

        Assert.Equal((CodigosOrden.CalibracionVencida, Msg013), (error.Codigo, error.Message));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Solo el Ingeniero Biomédico aprueba y firma las órdenes de trabajo.")]
    [InlineData(HttpStatusCode.NotFound, "Esta orden ya no está en tu institución. Actualiza la lista.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "No se pudo firmar porque el servidor de documentos no respondió. La orden sigue cerrada; intenta de nuevo.")]
    public async Task Aprobar_ErroresSinDetalle_DanUnMensajeClaro(HttpStatusCode estado, string mensaje)
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(estado));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AprobarAsync(OrdenId, "Laura Gómez", CancellationToken.None));

        Assert.Equal(mensaje, error.Message);
    }
}
