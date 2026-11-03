using System.Net;
using System.Net.Http;
using System.Text;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioUsuariosHttpTests
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

    private static (ServicioUsuariosHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioUsuariosHttp(new ClienteApi(http)), manejador);
    }

    private static string UsuarioJson(int n) =>
        $$"""{"id":"{{Guid.NewGuid()}}","nombres":"U{{n}}","apellidos":"P","correo":"u{{n}}@demo.co","telefono":null,"rol":"AC-03","estado":"Activo","debeCambiarClave":false,"ultimoAcceso":null}""";

    [Fact]
    public async Task Listar_RecorreTodasLasPaginas()
    {
        var pagina1 = string.Join(",", Enumerable.Range(1, 100).Select(UsuarioJson));
        var (servicio, manejador) = Crear(r => r.RequestUri!.Query.Contains("page=1&", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, $$"""{"items":[{{pagina1}}],"page":1,"pageSize":100,"total":101}""")
            : Json(HttpStatusCode.OK, $$"""{"items":[{{UsuarioJson(101)}}],"page":2,"pageSize":100,"total":101}"""));

        var usuarios = await servicio.ListarAsync(CancellationToken.None);

        Assert.Equal(101, usuarios.Count);
        Assert.Equal(["GET /api/v1/usuarios?page=1&pageSize=100", "GET /api/v1/usuarios?page=2&pageSize=100"], manejador.Rutas);
    }

    [Fact]
    public async Task Crear_Devuelve201ConLaClaveTemporal()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.Created, $$"""{"usuario":{{UsuarioJson(1)}},"claveTemporal":"Tmp-123"}"""));

        var creado = await servicio.CrearAsync(new DatosUsuario("U1", "P", "u1@demo.co", null, "AC-03"), CancellationToken.None);

        Assert.Equal("Tmp-123", creado.ClaveTemporal);
        Assert.Equal("U1", creado.Usuario.Nombres);
        Assert.Equal("POST /api/v1/usuarios", manejador.Rutas.Single());
    }

    [Fact]
    public async Task Conflicto_TraeElCodigoYElMensajeDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict,
            """{"title":"Conflicto","status":409,"detail":"Tu institución alcanzó el máximo del plan Clínica.","codigo":"RN-009"}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CambiarEstadoAsync(Guid.NewGuid(), "reactivar", CancellationToken.None));

        Assert.Equal("RN-009", error.Codigo);
        Assert.Equal("Tu institución alcanzó el máximo del plan Clínica.", error.Message);
    }

    [Fact]
    public async Task Validacion_TraeLosErroresPorCampo()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"Datos no válidos","status":400,"errors":{"correo":["Correo no válido."]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.CrearAsync(new DatosUsuario("A", "B", "x", null, "AC-03"), CancellationToken.None));

        Assert.Equal("Correo no válido.", error.Errores["correo"]);
    }

    [Fact]
    public async Task Prohibido_NoMuestraDetallesTecnicos()
    {
        var (servicio, _) = Crear(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ListarAsync(CancellationToken.None));

        Assert.Equal("Tu rol no permite gestionar usuarios.", error.Message);
    }

    [Fact]
    public async Task SinServidor_EsMsg011()
    {
        var (servicio, _) = Crear(_ => throw new HttpRequestException("rechazada"));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.AsignarClaveTemporalAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.True(error.SinConexion);
        Assert.DoesNotContain("rechazada", error.Message);
    }
}
