using System.Net;
using System.Net.Http;
using System.Text;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioEquiposHttpTests
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

    private static (ServicioEquiposHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioEquiposHttp(new ClienteApi(http)), manejador);
    }

    private const string PaginaVacia = """{"items":[],"page":1,"pageSize":20,"total":0}""";

    [Fact]
    public async Task SinFiltros_SoloEnviaLaPagina()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, PaginaVacia));

        await servicio.ListarAsync(new FiltroEquipos(), CancellationToken.None);

        Assert.Equal("GET /api/v1/equipos?page=1&pageSize=20", manejador.Rutas.Single());
    }

    [Fact]
    public async Task ConFiltros_EnviaSoloLosPresentesYEscapados()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, PaginaVacia));

        await servicio.ListarAsync(
            new FiltroEquipos(Serie: " SN 10/4 ", Estado: "OPERATIVO", Clase: "IIb", Calibracion: false, Pagina: 3),
            CancellationToken.None);

        Assert.Equal(
            "GET /api/v1/equipos?serie=SN%2010%2F4&estado=OPERATIVO&clase=IIb&calibracion=false&page=3&pageSize=20",
            manejador.Rutas.Single());
    }

    [Fact]
    public async Task LeeElEquipoConSuInformacionRegulatoriaYPlan()
    {
        var id = Guid.NewGuid();
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, $$"""
            {"id":"{{id}}","placaInventario":"HSV-EQ-0412","numeroSerie":"MX450-77812","marca":"Philips","modelo":"IntelliVue MX450",
             "tipoEquipoId":"{{Guid.NewGuid()}}","tipoEquipo":"Monitor de signos vitales","ubicacionId":"{{Guid.NewGuid()}}",
             "ubicacion":"UCI adultos","proveedorId":null,"proveedor":null,"estado":"OPERATIVO","estadoNombre":"Operativo",
             "fechaAdquisicion":"2022-03-15","fechaBaja":null,"tipoAutorizacion":"registro_sanitario","registroInvima":"INVIMA 2019DM-0019830",
             "venceRegistro":"2027-02-10","avisoRegistro":"PorVencer","claseRiesgo":"IIb","requiereCalibracion":true,
             "periodicidadCalibracionMeses":12,"tieneFoto":false,
             "preventivo":{"id":"{{Guid.NewGuid()}}","periodicidadDias":90,"ultimaEjecucion":null,"proximaFecha":"2026-11-10","vencido":false} }
            """));

        var equipo = await servicio.ObtenerAsync(id, CancellationToken.None);

        Assert.Equal($"GET /api/v1/equipos/{id}", manejador.Rutas.Single());
        Assert.Equal("HSV-EQ-0412", equipo.PlacaInventario);
        Assert.Equal(AvisosRegistro.PorVencer, equipo.AvisoRegistro);
        Assert.Equal(new DateOnly(2027, 2, 10), equipo.VenceRegistro);
        Assert.Equal(12, equipo.PeriodicidadCalibracionMeses);
        Assert.Equal(90, equipo.Preventivo!.PeriodicidadDias);
        Assert.Null(equipo.Proveedor);
    }

    [Fact]
    public async Task Prohibido_DaUnMensajeClaro()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"title":"Prohibido","status":403}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ListarAsync(new FiltroEquipos(), CancellationToken.None));

        Assert.Equal("Tu rol no permite consultar el inventario de equipos.", error.Message);
    }

    [Fact]
    public async Task NoEncontrado_NoRevelaSiEsDeOtraInstitucion()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.NotFound, """{"title":"No encontrado","status":404}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("El equipo no existe o no pertenece a tu institución.", error.Message);
    }

    [Fact]
    public async Task FiltroInvalido_MuestraElErrorDelCampo()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"status":400,"errors":{"clase":["La clase de riesgo debe ser I, IIa, IIb o III."]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ListarAsync(new FiltroEquipos(Clase: "X"), CancellationToken.None));

        Assert.Equal("La clase de riesgo debe ser I, IIa, IIb o III.", error.Message);
    }

    [Fact]
    public async Task SinConexion_UsaMsg011()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("sin red"));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ListarAsync(new FiltroEquipos(), CancellationToken.None));

        Assert.True(error.SinConexion);
    }
}
