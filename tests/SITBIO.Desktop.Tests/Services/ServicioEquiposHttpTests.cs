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

    // ---------- Registro y edición (AC-02) ----------

    private static DatosEquipo Datos() => new(
        Guid.Parse("11111111-0000-0000-0000-000000000001"), Guid.Parse("11111111-0000-0000-0000-000000000002"), null,
        "SN-100", "CSR-EQ-0100", "Philips", "MX450", new DateOnly(2022, 3, 15), "registro_sanitario", "INVIMA 1",
        new DateOnly(2028, 12, 31), "IIb", true, 12, new DatosPreventivo(90, null, new DateOnly(2026, 12, 1)));

    private static string EquipoJson(Guid id) => $$"""
        {"id":"{{id}}","placaInventario":"CSR-EQ-0100","numeroSerie":"SN-100","marca":"Philips","modelo":"MX450",
         "tipoEquipoId":"{{Guid.NewGuid()}}","tipoEquipo":"Monitor","ubicacionId":"{{Guid.NewGuid()}}","ubicacion":"UCI",
         "estado":"OPERATIVO","estadoNombre":"Operativo","avisoRegistro":"Vigente","requiereCalibracion":true,"tieneFoto":false}
        """;

    [Fact]
    public async Task Registrar_EnviaElCuerpoEnCamelCaseConFechasIso()
    {
        string? cuerpo = null;
        var (servicio, manejador) = Crear(solicitud =>
        {
            cuerpo = solicitud.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.Created, EquipoJson(Guid.NewGuid()));
        });

        var creado = await servicio.CrearAsync(Datos(), CancellationToken.None);

        Assert.Equal("POST /api/v1/equipos", manejador.Rutas.Single());
        Assert.Equal("CSR-EQ-0100", creado.PlacaInventario);
        Assert.Contains("\"placaInventario\":\"CSR-EQ-0100\"", cuerpo);
        Assert.Contains("\"venceRegistro\":\"2028-12-31\"", cuerpo);
        Assert.Contains("\"preventivo\":{\"periodicidadDias\":90,\"ultimaEjecucion\":null,\"proximaFecha\":\"2026-12-01\"}", cuerpo);
    }

    [Fact]
    public async Task Editar_UsaPutConElId()
    {
        var id = Guid.NewGuid();
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, EquipoJson(id)));

        var actualizado = await servicio.ActualizarAsync(id, Datos(), CancellationToken.None);

        Assert.Equal($"PUT /api/v1/equipos/{id}", manejador.Rutas.Single());
        Assert.Equal(id, actualizado.Id);
    }

    [Fact]
    public async Task Registrar_ConOtroRol_DaElMensajeDeGestion()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"status":403}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CrearAsync(Datos(), CancellationToken.None));

        Assert.Equal("Solo el Ingeniero Biomédico registra y edita equipos.", error.Message);
    }

    [Fact]
    public async Task Registrar_PlacaRepetida_ConservaElCodigoRn001()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"codigo":"RN-001","detail":"Ya existe un equipo con la placa de inventario CSR-EQ-0100 en tu institución."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CrearAsync(Datos(), CancellationToken.None));

        Assert.Equal("RN-001", error.Codigo);
        Assert.Equal("Ya existe un equipo con la placa de inventario CSR-EQ-0100 en tu institución.", error.Message);
    }

    [Fact]
    public async Task Catalogos_ConsultaPorTipoYLeeLosValoresDelTipo()
    {
        var manejador = new ManejadorFalso(_ => Json(HttpStatusCode.OK,
            $$"""[{"id":"{{Guid.NewGuid()}}","nombre":"Tensiómetro","activo":true,"claseRiesgo":"I","requiereCalibracion":true,"periodicidadCalibracionMeses":12}]"""));
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        var catalogos = new ServicioCatalogosHttp(new ClienteApi(http));

        var tipos = await catalogos.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None);

        Assert.Equal("GET /api/v1/catalogos/tipos-equipo", manejador.Rutas.Single());
        var tipo = Assert.Single(tipos);
        Assert.Equal(("Tensiómetro", "I", 12), (tipo.Nombre, tipo.ClaseRiesgo, tipo.PeriodicidadCalibracionMeses));
    }
}
