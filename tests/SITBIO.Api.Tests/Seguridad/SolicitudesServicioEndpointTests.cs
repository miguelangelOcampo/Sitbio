using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Api.Modules.Instituciones;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de GET /planes y POST /solicitudes-servicio (API-SERV-PLANES, API-SERV-SOL; HU-028):
/// públicos para demostración y cotización; el cambio de plan exige al Administrador. Sin base de datos.
/// </summary>
public class SolicitudesServicioEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/solicitudes-servicio";
    private readonly IRepositorioSolicitudesServicio _repositorio = Substitute.For<IRepositorioSolicitudesServicio>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();

    private static object Demostracion(string nit = "900.123.456-7") => new
    {
        tipo = "demostracion",
        institucion = "Clínica San Rafael",
        nit,
        contacto = "Ana Gómez",
        correo = "ana@sanrafael.co",
        planSolicitado = "Clínica",
    };

    private HttpClient Cliente(string? codigoRol, int limite = 100)
    {
        _repositorio.When(r => r.RegistrarAsync(Arg.Any<SolicitudServicio>(), Arg.Any<CancellationToken>()))
            .Do(llamada => llamada.Arg<SolicitudServicio>().Numerar(1));
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .UseSetting(SolicitudesServicioEndpoints.SeccionLimite, limite.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _repositorio)
                    .AddScoped(_ => _instituciones)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)))
            .CreateClient();

        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var usuario = new Usuario(Guid.NewGuid(), 1, "Sesión", "Prueba", "sesion@demo.co", "hash");
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Planes_SonPublicos()
    {
        var respuesta = await Cliente(null).GetAsync($"{RutasApi.V1}/planes");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var planes = (await Cuerpo(respuesta)).EnumerateArray().ToList();
        Assert.Equal(4, planes.Count);
        Assert.Equal("Consultorio", planes[0].GetProperty("nombre").GetString());
        Assert.Equal(300, planes[0].GetProperty("maxEquipos").GetInt32());
        Assert.Equal(JsonValueKind.Null, planes[3].GetProperty("valorMensualCop").ValueKind);
    }

    [Fact]
    public async Task Demostracion_SinToken_Responde201ConElCodigo()
    {
        var respuesta = await Cliente(null).PostAsJsonAsync(Ruta, Demostracion());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await Cuerpo(respuesta);
        Assert.Equal("SOL-000001", json.GetProperty("codigo").GetString());
        Assert.Equal("recibida", json.GetProperty("estado").GetString());
        Assert.EndsWith("/solicitudes-servicio/SOL-000001", respuesta.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatosInvalidos_Responde400PorCampo()
    {
        var respuesta = await Cliente(null).PostAsJsonAsync(Ruta, Demostracion(nit: "900123"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.True((await Cuerpo(respuesta)).GetProperty("errors").TryGetProperty("nit", out _));
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Fact]
    public async Task CambioDePlan_SinToken401_YConOtroRol403()
    {
        var cuerpo = new { tipo = "cambio_plan", contacto = "Ana", correo = "ana@sanrafael.co", planSolicitado = "Hospital" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null).PostAsJsonAsync(Ruta, cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(CodigosRol.IngenieroBiomedico).PostAsJsonAsync(Ruta, cuerpo)).StatusCode);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Fact]
    public async Task MasSolicitudesQueElLimitePorMinuto_Responde429()
    {
        var cliente = Cliente(null, limite: 2);

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync(Ruta, Demostracion())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync(Ruta, Demostracion())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await cliente.PostAsJsonAsync(Ruta, Demostracion())).StatusCode);
    }
}
