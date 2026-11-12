using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-CAT desde el cliente (HU-025): rutas, cuerpos y errores de la gestión de catálogos.</summary>
public class ServicioCatalogosHttpTests
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

    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioCatalogosHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioCatalogosHttp(new ClienteApi(http)), manejador);
    }

    [Fact]
    public async Task Crear_EnviaPostConElNombre()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.Created, $$"""{"id":"{{Id}}","nombre":"Urgencias","activo":true}"""));

        var creado = await servicio.CrearAsync(TiposCatalogo.Ubicaciones, new DatosCatalogo("Urgencias"), CancellationToken.None);

        Assert.Equal("POST /api/v1/catalogos/ubicaciones", manejador.Rutas.Single());
        Assert.Equal("Urgencias", JsonDocument.Parse(manejador.Cuerpo!).RootElement.GetProperty("nombre").GetString());
        Assert.Equal((Id, true), (creado.Id, creado.Activo));
    }

    [Fact]
    public async Task Actualizar_EnviaPutConTodosLosDatosYElEstado()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"{{Id}}","nombre":"Biomédica Andina","activo":false,"nit":"900.111.222-3","tipoProveedor":"Servicio técnico"}"""));
        var proveedor = new ElementoCatalogo(Id, "Biomédica Andina", true, Nit: "900.111.222-3", TipoProveedor: "Servicio técnico", Correo: "contacto@andina.co");

        var guardado = await servicio.ActualizarAsync(TiposCatalogo.Proveedores, Id, proveedor.ComoDatos(activo: false), CancellationToken.None);

        Assert.Equal($"PUT /api/v1/catalogos/proveedores/{Id}", manejador.Rutas.Single());
        var cuerpo = JsonDocument.Parse(manejador.Cuerpo!).RootElement;
        Assert.Equal(
            ("900.111.222-3", "Servicio técnico", "contacto@andina.co", false),
            (cuerpo.GetProperty("nit").GetString(), cuerpo.GetProperty("tipoProveedor").GetString(), cuerpo.GetProperty("correo").GetString(), cuerpo.GetProperty("activo").GetBoolean()));
        Assert.False(guardado.Activo);
    }

    [Fact]
    public async Task Obtener_LeeElDetalle()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"{{Id}}","nombre":"Monitor","activo":true,"claseRiesgo":"IIb","requiereCalibracion":true,"periodicidadCalibracionMeses":6,"descripcion":"Signos vitales"}"""));

        var tipo = await servicio.ObtenerAsync(TiposCatalogo.TiposEquipo, Id, CancellationToken.None);

        Assert.Equal($"GET /api/v1/catalogos/tipos-equipo/{Id}", manejador.Rutas.Single());
        Assert.Equal(("IIb", 6, "Signos vitales"), (tipo.ClaseRiesgo, tipo.PeriodicidadCalibracionMeses, tipo.Descripcion));
    }

    [Fact]
    public async Task Crear_Duplicado_TraeElCodigoYElMensajeDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"codigo":"CAT-001","detail":"«Urgencias» ya está en las ubicaciones de tu institución."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CrearAsync(TiposCatalogo.Ubicaciones, new DatosCatalogo("Urgencias"), CancellationToken.None));

        Assert.Equal("CAT-001", error.Codigo);
        Assert.Equal("«Urgencias» ya está en las ubicaciones de tu institución.", error.Message);
    }

    [Fact]
    public async Task Crear_SinPermiso_MuestraElDetalleDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden,
            """{"status":403,"detail":"Solo el Ingeniero Biomédico gestiona los tipos de equipo; puedes consultarlos."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CrearAsync(TiposCatalogo.TiposEquipo, new DatosCatalogo("Desfibrilador"), CancellationToken.None));

        Assert.Equal("Solo el Ingeniero Biomédico gestiona los tipos de equipo; puedes consultarlos.", error.Message);
    }
}
