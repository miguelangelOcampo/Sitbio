using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-TIQ desde el cliente (HU-003, HU-018): rutas, cuerpo del reporte y errores.</summary>
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
}
