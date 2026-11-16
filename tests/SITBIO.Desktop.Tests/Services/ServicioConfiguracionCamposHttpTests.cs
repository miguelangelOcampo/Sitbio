using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>API-CFG desde el cliente (HU-006): ruta, cuerpo de la publicación y errores.</summary>
public class ServicioConfiguracionCamposHttpTests
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

    private static readonly Guid Tipo = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioConfiguracionCamposHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioConfiguracionCamposHttp(new ClienteApi(http)), manejador);
    }

    private static readonly string Respuesta = $$"""
        {"tipoEquipoId":"{{Tipo}}","tipoEquipo":"Monitor","version":2,"fechaPublicacion":"2026-11-12T10:00:00+00:00",
         "campos":[{"nombre":"presion_sistolica","etiqueta":"Presión sistólica","tipoDato":"numero","obligatorio":true,"unidad":"mmHg"},
                   {"nombre":"estado","etiqueta":"Estado","tipoDato":"seleccion","obligatorio":false,"opciones":["Bueno","Malo"]}],
         "versiones":[{"version":2,"estado":"activa","fechaCreacion":"2026-11-12T10:00:00+00:00","campos":2},
                      {"version":1,"estado":"historica","fechaCreacion":"2026-11-01T10:00:00+00:00","campos":1}]}
        """;

    [Fact]
    public async Task Obtener_LeeLaVersionActivaYElHistorial()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, Respuesta));

        var configuracion = await servicio.ObtenerAsync(Tipo, CancellationToken.None);

        Assert.Equal($"GET /api/v1/configuracion-campos/{Tipo}", manejador.Rutas.Single());
        Assert.Equal(2, configuracion.Version);
        Assert.Equal(("presion_sistolica", "mmHg", true), (configuracion.Campos[0].Nombre, configuracion.Campos[0].Unidad, configuracion.Campos[0].Obligatorio));
        Assert.Equal(["Bueno", "Malo"], configuracion.Campos[1].Opciones!);
        Assert.Equal([2, 1], configuracion.Versiones.Select(v => v.Version));
    }

    [Fact]
    public async Task Obtener_SinVersion_DevuelveVersionNula()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.OK,
            $$"""{"tipoEquipoId":"{{Tipo}}","tipoEquipo":"Monitor","version":null,"fechaPublicacion":null,"campos":[],"versiones":[]}"""));

        var configuracion = await servicio.ObtenerAsync(Tipo, CancellationToken.None);

        Assert.Null(configuracion.Version);
        Assert.Empty(configuracion.Campos);
    }

    [Fact]
    public async Task Publicar_EnviaPostConLaListaCompletaDeCampos()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.Created, Respuesta));
        CampoDinamico[] campos =
        [
            new("presion_sistolica", "Presión sistólica", TiposDatoCampo.Numero, true, Unidad: "mmHg"),
            new("estado", "Estado", TiposDatoCampo.Seleccion, false, ["Bueno", "Malo"]),
        ];

        var publicada = await servicio.PublicarAsync(Tipo, campos, CancellationToken.None);

        Assert.Equal($"POST /api/v1/configuracion-campos/{Tipo}", manejador.Rutas.Single());
        var enviados = JsonDocument.Parse(manejador.Cuerpo!).RootElement.GetProperty("campos");
        Assert.Equal(2, enviados.GetArrayLength());
        Assert.Equal(
            ("presion_sistolica", "Presión sistólica", "numero", true, "mmHg"),
            (enviados[0].GetProperty("nombre").GetString(), enviados[0].GetProperty("etiqueta").GetString(),
             enviados[0].GetProperty("tipoDato").GetString(), enviados[0].GetProperty("obligatorio").GetBoolean(), enviados[0].GetProperty("unidad").GetString()));
        Assert.Equal(2, enviados[1].GetProperty("opciones").GetArrayLength());
        Assert.Equal(2, publicada.Version);
    }

    [Fact]
    public async Task Publicar_SinPermiso_MuestraElDetalleDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden,
            """{"status":403,"detail":"Solo el Ingeniero Biomédico define y publica los campos dinámicos; puedes consultarlos."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.PublicarAsync(Tipo, [], CancellationToken.None));

        Assert.Equal("Solo el Ingeniero Biomédico define y publica los campos dinámicos; puedes consultarlos.", error.Message);
    }

    [Fact]
    public async Task Publicar_VersionConcurrente_TraeElCodigoCFG001()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"codigo":"CFG-001","detail":"Otra persona publicó una versión al mismo tiempo. Recarga y vuelve a intentarlo."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.PublicarAsync(Tipo, [], CancellationToken.None));

        Assert.Equal("CFG-001", error.Codigo);
    }

    [Fact]
    public async Task Obtener_TipoInexistente_UsaElMensajeDelCliente()
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ObtenerAsync(Tipo, CancellationToken.None));

        Assert.Equal("Ese tipo de equipo ya no existe en tu institución. Actualiza la lista.", error.Message);
    }
}
