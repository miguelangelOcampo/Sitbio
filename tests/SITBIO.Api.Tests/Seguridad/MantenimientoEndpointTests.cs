using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de API-PLAN y API-ALERTAS (HU-019, HU-020): solo el Ingeniero (AC-02) crea y modifica
/// planes; los consultan AC-01, AC-02, AC-03, AC-05 y AC-07; las alertas, AC-02 y AC-03. Sin base de datos.
/// </summary>
public class MantenimientoEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Planes = $"{RutasApi.V1}/planes-preventivos";
    private static readonly string Alertas = $"{RutasApi.V1}/alertas";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IRepositorioPlanes _planes = Substitute.For<IRepositorioPlanes>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly PlanPreventivo _plan;
    private readonly EquipoDelPlan _equipo = new(Guid.NewGuid(), "PL-001", false);

    public MantenimientoEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _plan = new PlanPreventivo(Guid.NewGuid(), _equipo.Id, "Preventivo mensual", 30, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), DateTimeOffset.UtcNow);
        var leido = new PlanLeido(_plan, "PL-001", "Bomba", "UCI");
        _planes.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(leido);
        _planes.PlanAsync(_plan.Id, Arg.Any<CancellationToken>()).Returns(_plan);
        _planes.EquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(_equipo);
        _planes.ListarAsync(Arg.Any<FiltroPlanes>(), Arg.Any<CancellationToken>()).Returns(([leido], 1));
        _planes.DiasAnticipacionAsync(Arg.Any<CancellationToken>()).Returns(15);
        _planes.GenerarOrdenesAsync(
                Arg.Any<DateOnly>(),
                Arg.Any<Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        _planes.PlanesPorVencerAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([new PlanPorVencer(leido, null, null)]);
        _planes.CalibracionesPorVencerAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = _fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _planes)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)))
            .CreateClient();

        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    [Fact]
    public async Task Ingeniero_CreaConsultaYSuspendeUnPlan()
    {
        var cliente = Cliente(CodigosRol.IngenieroBiomedico);

        var creado = await cliente.PostAsJsonAsync(Planes, new { equipoId = _equipo.Id, periodicidadDias = 30, proximaFecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20) });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.StartsWith($"{RutasApi.V1}/planes-preventivos/", creado.Headers.Location!.OriginalString);
        _planes.Received(1).Agregar(Arg.Is<PlanPreventivo>(p => p.EquipoId == _equipo.Id && p.PeriodicidadDias == 30));

        var pagina = await cliente.GetFromJsonAsync<JsonElement>($"{Planes}?estado=activo&equipoId={_equipo.Id}");
        Assert.Equal(1, pagina.GetProperty("total").GetInt32());

        var suspendido = await cliente.PutAsJsonAsync($"{Planes}/{_plan.Id}", new { estado = "suspendido" });
        Assert.Equal(HttpStatusCode.OK, suspendido.StatusCode);
        Assert.Equal(PlanPreventivo.Suspendido, _plan.Estado);

        // Sin periodicidad: 400 junto al campo.
        var invalido = await cliente.PostAsJsonAsync(Planes, new { equipoId = _equipo.Id, proximaFecha = DateOnly.FromDateTime(DateTime.UtcNow) });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Contains("periodicidadDias", await invalido.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task OtrosRolesQueConsultan_VenLosPlanes_PeroNoLosModifican(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync(Planes)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"{Planes}/{_plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync(Planes, new { equipoId = _equipo.Id, periodicidadDias = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync($"{Planes}/{_plan.Id}", new { estado = "suspendido" })).StatusCode);
        Assert.Equal(PlanPreventivo.Activo, _plan.Estado);
    }

    [Theory]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(null)]
    public async Task SinPermiso_NiConsultaPlanesNiAlertas(string? rol)
    {
        var cliente = Cliente(rol);
        var esperado = rol is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;

        Assert.Equal(esperado, (await cliente.GetAsync(Planes)).StatusCode);
        Assert.Equal(esperado, (await cliente.GetAsync(Alertas)).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.IngenieroBiomedico, HttpStatusCode.OK)]
    [InlineData(CodigosRol.TecnicoMantenimiento, HttpStatusCode.OK)]
    [InlineData(CodigosRol.AdministradorSistema, HttpStatusCode.Forbidden)]
    [InlineData(CodigosRol.GerenteBiomedico, HttpStatusCode.Forbidden)]
    [InlineData(CodigosRol.EnteRegulador, HttpStatusCode.Forbidden)]
    public async Task Alertas_SoloIngenieroYTecnico(string rol, HttpStatusCode esperado)
    {
        var respuesta = await Cliente(rol).GetAsync(Alertas);

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.OK)
        {
            var cuerpo = (await respuesta.Content.ReadFromJsonAsync<AlertasDto>(Json))!;
            Assert.Equal((15, 0), (cuerpo.DiasAnticipacion, cuerpo.OrdenesGeneradas));
            var alerta = Assert.Single(cuerpo.Alertas);
            Assert.Equal((TiposAlerta.Preventivo, EstadosAlerta.Proxima, _plan.Id), (alerta.Tipo, alerta.Estado, alerta.PlanId!.Value));
        }
    }
}
