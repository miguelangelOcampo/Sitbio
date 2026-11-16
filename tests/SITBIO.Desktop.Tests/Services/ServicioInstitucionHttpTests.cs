using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public class ServicioInstitucionHttpTests
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

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (ServicioInstitucionHttp Servicio, ManejadorFalso Manejador) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manejador = new ManejadorFalso(responder);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost:5080/api/v1/") };
        return (new ServicioInstitucionHttp(new ClienteApi(http)), manejador);
    }

    private const string Tenant = """
        {"id":"11111111-1111-1111-1111-111111111111","nombre":"Clínica San Rafael","nit":"900.123.456-7","diasAnticipacionAlerta":15,
         "plan":"Clínica","vigenteHasta":"2027-06-30",
         "consumo":{"usuariosGestion":{"usados":6,"maximo":7,"porcentaje":85,"alerta":true,"limiteAlcanzado":false},
                    "equiposActivos":{"usados":420,"maximo":1000,"porcentaje":42,"alerta":false,"limiteAlcanzado":false},
                    "almacenamientoGb":{"usados":1.25,"maximo":25,"porcentaje":5,"alerta":false,"limiteAlcanzado":false},
                    "hayAlerta":true}}
        """;

    [Fact]
    public async Task Obtener_LeeLosDatosElPlanYElConsumo()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, Tenant));

        var institucion = await servicio.ObtenerAsync(CancellationToken.None);

        Assert.Equal("GET /api/v1/tenant/actual", manejador.Rutas.Single());
        Assert.Equal(("Clínica San Rafael", "900.123.456-7", 15), (institucion.Nombre, institucion.Nit, institucion.DiasAnticipacionAlerta));
        Assert.Equal(("Clínica", new DateOnly(2027, 6, 30)), (institucion.Plan, institucion.VigenteHasta));
        Assert.Equal(new MedidorPlan(6, 7, 85, true, false), institucion.Consumo.UsuariosGestion);
        Assert.Equal(1.25m, institucion.Consumo.AlmacenamientoGb.Usados);
        Assert.True(institucion.Consumo.HayAlerta);
    }

    [Fact]
    public async Task Actualizar_EnviaPutConLosTresDatos()
    {
        var (servicio, manejador) = Crear(_ => Json(HttpStatusCode.OK, Tenant));

        await servicio.ActualizarAsync(new DatosInstitucion("Clínica San Rafael", "900.123.456-7", 30), CancellationToken.None);

        Assert.Equal("PUT /api/v1/tenant/actual", manejador.Rutas.Single());
        var json = JsonDocument.Parse(manejador.Cuerpo!).RootElement;
        Assert.Equal("Clínica San Rafael", json.GetProperty("nombre").GetString());
        Assert.Equal("900.123.456-7", json.GetProperty("nit").GetString());
        Assert.Equal(30, json.GetProperty("diasAnticipacionAlerta").GetInt32());
    }

    [Fact]
    public async Task Responde403_ExplicaQueSoloElAdministrador()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Forbidden, """{"status":403}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() => servicio.ObtenerAsync(CancellationToken.None));

        Assert.Equal("Solo el Administrador del Sistema configura la institución.", error.Message);
    }

    [Fact]
    public async Task NitDeOtraInstitucion_TraeElCodigoYElTextoDeLaApi()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"codigo":"TEN-001","detail":"El NIT 800.111.222-3 ya está registrado en SITBIO."}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.ActualizarAsync(new DatosInstitucion("Clínica San Rafael", "800.111.222-3", 15), CancellationToken.None));

        Assert.Equal("TEN-001", error.Codigo);
        Assert.Equal("El NIT 800.111.222-3 ya está registrado en SITBIO.", error.Message);
    }

    [Fact]
    public async Task ErrorPorCampo_LlegaEnErrores()
    {
        var (servicio, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"status":400,"errors":{"nit":["Usa el formato 900.000.000-0."]}}"""));

        var error = await Assert.ThrowsAsync<ErrorApi>(() =>
            servicio.ActualizarAsync(new DatosInstitucion("Clínica San Rafael", "900", 15), CancellationToken.None));

        Assert.Equal("Usa el formato 900.000.000-0.", error.Errores["nit"]);
    }
}
