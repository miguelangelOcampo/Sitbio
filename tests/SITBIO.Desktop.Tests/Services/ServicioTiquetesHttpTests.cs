using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-TIQ desde el cliente (HU-003, HU-017, HU-018): rutas, cuerpo del reporte, seguimiento y errores.</summary>
public class ServicioTiquetesHttpTests
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

    private static readonly Guid Equipo = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioTiquetesHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioTiquetesHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task TiposFalla_LeeElCatalogoParaReportar()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, """["Alarma","No enciende"]"""));

        var tipos = await servicio.TiposFallaAsync(CancellationToken.None);

        Assert.Equal("GET /api/v1/tiquetes/tipos-falla", manejador.Rutas.Single());
        Assert.Equal(["Alarma", "No enciende"], tipos);
    }

    [Fact]
    public async Task Reportar_EnviaLosCuatroDatosYLeeElCodigo()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.Created, $$"""
            {"id":"{{Guid.NewGuid()}}","codigo":"TQT-000015","equipoId":"{{Equipo}}","placaInventario":"HSV-EQ-0412","numeroSerie":"MX-1",
             "tipoEquipo":"Monitor","ubicacion":"UCI","tipoFalla":"No enciende","descripcion":"","criticidad":"critica",
             "prioridadSugerida":"critica","estado":"recibido","fechaReporte":"2026-11-13T15:00:00+00:00","fechaCierre":null,
             "reportanteId":"{{Guid.NewGuid()}}","reportante":"Laura Gómez","estadoEquipo":"FUERA_DE_SERVICIO","equipoFueraDeServicio":true}
            """));

        var tiquete = await servicio.ReportarAsync(new DatosTiquete(Equipo, "No enciende", null, Criticidades.Critica), CancellationToken.None);

        Assert.Equal("POST /api/v1/tiquetes", manejador.Rutas.Single());
        var cuerpo = JsonDocument.Parse(manejador.Cuerpo!).RootElement;
        Assert.Equal(
            (Equipo, "No enciende", Criticidades.Critica, JsonValueKind.Null),
            (cuerpo.GetProperty("equipoId").GetGuid(), cuerpo.GetProperty("tipoFalla").GetString(), cuerpo.GetProperty("criticidad").GetString(), cuerpo.GetProperty("descripcion").ValueKind));
        Assert.Equal(("TQT-000015", true), (tiquete.Codigo, tiquete.EquipoFueraDeServicio));
    }

    [Fact]
    public async Task Reportar_SinEquipo_TraeElErrorDelCampo()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"status":400,"detail":"Uno o más datos no son válidos.","errors":{"equipoId":["Selecciona el equipo que presenta la falla."]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.ReportarAsync(new DatosTiquete(Guid.Empty, "No enciende", null, Criticidades.Baja), CancellationToken.None));

        Assert.Equal("Selecciona el equipo que presenta la falla.", error.Errores["equipoId"]);
    }

    [Fact]
    public async Task Reportar_SinPermiso_UsaElMensajeDelCliente()
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.ReportarAsync(new DatosTiquete(Equipo, "No enciende", null, Criticidades.Baja), CancellationToken.None));

        Assert.Equal("Tu rol no permite reportar fallas.", error.Message);
    }

    private static string TiqueteJson(string codigo, string estado) => $$"""
        {"id":"{{Guid.NewGuid()}}","codigo":"{{codigo}}","equipoId":"{{Equipo}}","placaInventario":"HSV-EQ-0412","numeroSerie":"MX-1",
         "tipoEquipo":"Monitor","ubicacion":"UCI","tipoFalla":"No enciende","descripcion":"","criticidad":"alta",
         "prioridadSugerida":"alta","estado":"{{estado}}","fechaReporte":"2026-11-13T15:00:00+00:00","fechaCierre":null,
         "reportanteId":"{{Guid.NewGuid()}}","reportante":"Laura Gómez","estadoEquipo":"OPERATIVO","equipoFueraDeServicio":false}
        """;

    [Theory]
    [InlineData(null, false, 1, "GET /api/v1/tiquetes?page=1&pageSize=20")]
    [InlineData("en_ejecucion", false, 2, "GET /api/v1/tiquetes?page=2&pageSize=20&estado=en_ejecucion")]
    [InlineData(null, true, 1, "GET /api/v1/tiquetes?page=1&pageSize=20&mios=true")]
    public async Task Listar_ArmaLaRutaConLosFiltros(string? estado, bool mios, int pagina, string ruta)
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK,
            $$"""{"items":[{{TiqueteJson("TQT-000002", "asignado")}}],"page":{{pagina}},"pageSize":20,"total":21}"""));

        var resultado = await servicio.ListarAsync(new FiltroTiquetes(estado, mios, pagina), CancellationToken.None);

        Assert.Equal(ruta, manejador.Rutas.Single());
        Assert.Equal((21, "TQT-000002", "asignado"), (resultado.Total, resultado.Items[0].Codigo, resultado.Items[0].Estado));
    }

    [Fact]
    public async Task ObtenerEHistorial_LeenElTiqueteYSusCambios()
    {
        var (servicio, manejador) = Crear(solicitud => solicitud.RequestUri!.AbsolutePath.EndsWith("/historial", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, $$"""
                [{"estadoAnterior":null,"nuevoEstado":"recibido","comentario":"Falla reportada.","fechaCambio":"2026-11-13T15:00:00+00:00","usuarioId":"{{Guid.NewGuid()}}","usuario":"Laura Gómez"},
                 {"estadoAnterior":"recibido","nuevoEstado":"asignado","comentario":null,"fechaCambio":"2026-11-13T15:10:00+00:00","usuarioId":"{{Guid.NewGuid()}}","usuario":"Carlos Ruiz"}]
                """)
            : Json(HttpStatusCode.OK, TiqueteJson("TQT-000015", "asignado")));

        var tiquete = await servicio.ObtenerAsync("TQT-000015", CancellationToken.None);
        var historial = await servicio.HistorialAsync("TQT-000015", CancellationToken.None);

        Assert.Equal(["GET /api/v1/tiquetes/TQT-000015", "GET /api/v1/tiquetes/TQT-000015/historial"], manejador.Rutas);
        Assert.Equal("asignado", tiquete.Estado);
        Assert.Equal(
            [((string?)null, "recibido", "Laura Gómez"), ("recibido", "asignado", "Carlos Ruiz")],
            historial.Select(c => (c.EstadoAnterior, c.NuevoEstado, c.Usuario)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "No encontramos un tiquete con ese código entre los que puedes consultar. Revisa el código.")]
    [InlineData(HttpStatusCode.Forbidden, "Tu rol no permite consultar tiquetes.")]
    public async Task Obtener_SinAcceso_UsaElMensajeDeConsulta(HttpStatusCode estado, string mensaje)
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(estado));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.HistorialAsync("TQT-000099", CancellationToken.None));

        Assert.Equal(mensaje, error.Message);
    }
}
