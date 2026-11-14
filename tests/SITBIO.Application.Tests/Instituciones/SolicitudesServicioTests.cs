using System.Reflection;
using NSubstitute;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Application.Tests.Instituciones;

/// <summary>Consulta de planes y solicitud del servicio (HU-028, RF-029, RN-009).</summary>
public class SolicitudesServicioTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private readonly IRepositorioSolicitudesServicio _repositorio = Substitute.For<IRepositorioSolicitudesServicio>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly TimeProvider _tiempo = Substitute.For<TimeProvider>();
    private readonly SolicitudesServicio _caso;

    public SolicitudesServicioTests()
    {
        _tiempo.GetUtcNow().Returns(Ahora);
        _repositorio.When(r => r.RegistrarAsync(Arg.Any<SolicitudServicio>(), Arg.Any<CancellationToken>()))
            .Do(llamada => llamada.Arg<SolicitudServicio>().Numerar(7));
        _caso = new SolicitudesServicio(_repositorio, _instituciones, _tiempo);
    }

    private static DatosSolicitudServicio Datos(string tipo = "demostracion", string? plan = null, string? nit = "900.123.456-7") =>
        new(tipo, "Clínica San Rafael", nit, "Ana Gómez", "ana@sanrafael.co", "6041234567", plan, "Queremos verlo funcionando.");

    private void Institucion(string plan, int usuarios, int equipos, long bytes)
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        void Fijar(string propiedad, object? valor) =>
            typeof(Tenant).GetProperty(propiedad)!.SetValue(tenant, valor, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
        Fijar(nameof(Tenant.Id), Guid.NewGuid());
        Fijar(nameof(Tenant.Plan), plan);
        tenant.ActualizarDatos("Hospital del Norte", "900.555.444-1", 15);
        _instituciones.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(tenant);
        _instituciones.UsoAsync(Arg.Any<CancellationToken>()).Returns(new UsoPlan(usuarios, equipos, bytes));
    }

    [Fact]
    public void Planes_DevuelveLosCuatroPlanesConSusLimites()
    {
        var planes = SolicitudesServicio.Planes();

        Assert.Equal(4, planes.Count);
        Assert.Equal(new PlanServicioDto("Clínica", 7, 1000, 25, 380_000m), planes[1]);
        Assert.Null(planes[3].ValorMensualCop);
    }

    // ---------- CA-HU-028-01 ----------

    [Fact]
    public async Task CA_HU_028_01_SolicitudSinCuenta_SeRegistraYDevuelveElCodigo()
    {
        var creada = await _caso.SolicitarAsync(Datos(plan: "Hospital"), CancellationToken.None);

        Assert.Equal("SOL-000007", creada.Codigo);
        Assert.Equal("recibida", creada.Estado);
        Assert.Equal("Hospital", creada.PlanSolicitado);
        Assert.Equal(Ahora, creada.FechaCreacion);
        await _repositorio.Received(1).RegistrarAsync(
            Arg.Is<SolicitudServicio>(s => s.TenantId == null && s.Institucion == "Clínica San Rafael"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CA_HU_028_01_DatosInvalidos_Responde400PorCampoYNoGuarda()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.SolicitarAsync(Datos(nit: "123"), CancellationToken.None));

        Assert.True(error.Errores.ContainsKey("nit"));
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Fact]
    public async Task CA_HU_028_01_ElCambioDePlanNoEntraPorLaSolicitudPublica()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.SolicitarAsync(Datos(tipo: "cambio_plan", plan: "Hospital"), CancellationToken.None));

        Assert.True(error.Errores.ContainsKey("tipo"));
    }

    // ---------- CA-HU-028-02 ----------

    [Fact]
    public async Task CA_HU_028_02_ConsumoMayorQueElPlanElegido_NoEnviaEIndicaElLimite()
    {
        Institucion("Hospital", usuarios: 5, equipos: 420, bytes: 2 * Gb);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.SolicitarCambioPlanAsync(Datos(tipo: "cambio_plan", plan: "Consultorio"), CancellationToken.None));

        Assert.Equal("RN-009", error.Codigo);
        Assert.Contains("usuarios de gestión 5 de 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("equipos activos 420 de 300", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("almacenamiento", error.Message, StringComparison.Ordinal);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Fact]
    public async Task CA_HU_028_02_ConsumoQueCabe_EnviaLaSolicitudDeLaInstitucionDelToken()
    {
        Institucion("Clínica", usuarios: 3, equipos: 300, bytes: 5 * Gb);

        var creada = await _caso.SolicitarCambioPlanAsync(Datos(tipo: "cambio_plan", plan: "Consultorio"), CancellationToken.None);

        Assert.Equal("cambio_plan", creada.Tipo);
        await _repositorio.Received(1).RegistrarAsync(
            Arg.Is<SolicitudServicio>(s => s.TenantId != null && s.Institucion == "Hospital del Norte" && s.Nit == "900.555.444-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LimitesExcedidos_RedDeSaludNoTieneTope_YElAlmacenamientoSeCompara()
    {
        Assert.Empty(SolicitudesServicio.LimitesExcedidos(PlanServicio.Buscar("Red de Salud")!, new UsoPlan(500, 90_000, 900 * Gb)));
        var excedidos = SolicitudesServicio.LimitesExcedidos(PlanServicio.Buscar("Consultorio")!, new UsoPlan(1, 10, 6 * Gb));
        Assert.Equal(["almacenamiento 6 GB de 5 GB"], excedidos);
    }
}
