using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Application.Mantenimiento;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Mantenimiento;

/// <summary>
/// Planes preventivos (HU-019, CA-HU-019-01): el plan con periodicidad y próxima fecha queda asociado al
/// equipo; el próximo es el último + la periodicidad, ajustable; se suspende, reactiva o finaliza.
/// </summary>
public class GestionPlanesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // 13/11/2026 a las 10:00 en Colombia.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 13);

    private readonly IRepositorioPlanes _repositorio = Substitute.For<IRepositorioPlanes>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _ingeniero = Guid.NewGuid();
    private readonly EquipoDelPlan _equipo = new(Guid.NewGuid(), "PL-001", false);
    private readonly GestionPlanes _caso;
    private PlanPreventivo? _agregado;

    public GestionPlanesTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_ingeniero);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _repositorio.EquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(_equipo);
        _repositorio.When(r => r.Agregar(Arg.Any<PlanPreventivo>())).Do(llamada =>
        {
            _agregado = llamada.Arg<PlanPreventivo>();
            _repositorio.ObtenerAsync(_agregado.Id, Arg.Any<CancellationToken>()).Returns(Leido(_agregado));
        });
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionPlanes(_repositorio, _auditoria, _contexto, tiempo);
    }

    private static PlanLeido Leido(PlanPreventivo plan) => new(plan, "PL-001", "Bomba de infusión", "UCI");

    private PlanPreventivo Existente(DateOnly proxima, DateOnly? ultima = null)
    {
        var plan = new PlanPreventivo(Tenant, _equipo.Id, "Preventivo mensual", 30, proxima, Ahora.AddDays(-60), ultima);
        _repositorio.PlanAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _repositorio.ObtenerAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(Leido(plan));
        return plan;
    }

    [Fact]
    public async Task Crear_ConElUltimo_CalculaElProximo_YQuedaActivoYAsociadoAlEquipo()
    {
        var dto = await _caso.CrearAsync(new DatosPlan(_equipo.Id, " Preventivo semestral ", 180, Hoy.AddDays(-10), null), CancellationToken.None);

        Assert.NotNull(_agregado);
        Assert.Equal((Tenant, _equipo.Id), (_agregado.TenantId, _agregado.EquipoId));
        Assert.Equal((_equipo.Id, "PL-001", "Preventivo semestral", 180), (dto.EquipoId, dto.PlacaInventario, dto.Descripcion, dto.PeriodicidadDias));
        Assert.Equal(((DateOnly?)Hoy.AddDays(-10), Hoy.AddDays(170), PlanPreventivo.Activo, false), (dto.UltimaEjecucion, dto.ProximaFecha, dto.Estado, dto.Vencido));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _ingeniero,
            AccionesAuditoria.Crear,
            GestionPlanes.Entidad,
            _agregado.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["equipo"] == "PL-001" && d["proximaFecha"] == "2027-05-02"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_ElProximoIndicadoSeRespeta_YSinDescripcionUsaLaDeSiempre()
    {
        var dto = await _caso.CrearAsync(new DatosPlan(_equipo.Id, null, 90, Hoy.AddDays(-30), Hoy.AddDays(45)), CancellationToken.None);

        Assert.Equal((Hoy.AddDays(45), GestionEquipos.DescripcionPreventivo), (dto.ProximaFecha, dto.Descripcion));
    }

    [Fact]
    public async Task Crear_ConElUltimoAtrasado_NaceVencido()
    {
        var dto = await _caso.CrearAsync(new DatosPlan(_equipo.Id, null, 30, Hoy.AddDays(-45), null), CancellationToken.None);

        Assert.Equal((Hoy.AddDays(-15), true), (dto.ProximaFecha, dto.Vencido));
    }

    [Theory]
    [InlineData(null, 30, null, null, "equipoId")]
    [InlineData("equipo", 0, null, "2026-12-01", "periodicidadDias")]
    [InlineData("equipo", 30, "2026-11-20", null, "ultimaEjecucion")]
    [InlineData("equipo", 30, null, null, "proximaFecha")]
    [InlineData("equipo", 30, null, "2026-11-01", "proximaFecha")]
    [InlineData("equipo", 30, "2026-11-01", "2026-10-30", "proximaFecha")]
    public async Task Crear_DatosInvalidos_Responde400ConElCampo_YNoGuarda(string? equipo, int dias, string? ultima, string? proxima, string campo)
    {
        var datos = new DatosPlan(
            equipo is null ? null : _equipo.Id,
            null,
            dias,
            ultima is null ? null : DateOnly.Parse(ultima, System.Globalization.CultureInfo.InvariantCulture),
            proxima is null ? null : DateOnly.Parse(proxima, System.Globalization.CultureInfo.InvariantCulture));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(datos, CancellationToken.None));

        Assert.Contains(campo, error.Errores.Keys);
        Assert.Null(_agregado);
    }

    [Fact]
    public async Task Crear_EquipoDadoDeBaja_Responde409_YDeOtraInstitucion404()
    {
        var deBaja = new EquipoDelPlan(Guid.NewGuid(), "PL-009", true);
        _repositorio.EquipoAsync(deBaja.Id, Arg.Any<CancellationToken>()).Returns(deBaja);

        var conflicto = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CrearAsync(new DatosPlan(deBaja.Id, null, 30, null, Hoy), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.CrearAsync(new DatosPlan(Guid.NewGuid(), null, 30, null, Hoy), CancellationToken.None));

        Assert.Equal(GestionPlanes.CodigoEquipoDeBaja, conflicto.Codigo);
        Assert.Null(_agregado);
    }

    [Fact]
    public async Task Actualizar_CambiaLaPeriodicidad_YRecalculaDesdeElUltimo()
    {
        var plan = Existente(Hoy.AddDays(20), Hoy.AddDays(-10));

        var dto = await _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, 60, null, null, null), CancellationToken.None);

        Assert.Equal((60, Hoy.AddDays(50), "Preventivo mensual"), (dto.PeriodicidadDias, dto.ProximaFecha, dto.Descripcion));
        await _auditoria.Received(1).RegistrarAsync(
            _ingeniero, AccionesAuditoria.Actualizar, GestionPlanes.Entidad, plan.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Actualizar_PlanVencidoSinUltimo_ConservaSuProximaAlCambiarLaPeriodicidad()
    {
        var plan = Existente(Hoy.AddDays(-3));

        var dto = await _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan("Revisión general", 45, null, null, null), CancellationToken.None);

        Assert.Equal((45, Hoy.AddDays(-3), "Revisión general", true), (dto.PeriodicidadDias, dto.ProximaFecha, dto.Descripcion, dto.Vencido));
    }

    [Fact]
    public async Task Actualizar_SuspendeReactivaYFinaliza_YElFinalizadoYaNoCambia()
    {
        var plan = Existente(Hoy.AddDays(20));

        Assert.Equal(PlanPreventivo.Suspendido, (await _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, null, null, null, "Suspendido"), CancellationToken.None)).Estado);
        Assert.Equal(PlanPreventivo.Activo, (await _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, null, null, null, "activo"), CancellationToken.None)).Estado);
        Assert.Equal(PlanPreventivo.Finalizado, (await _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, null, null, null, "finalizado"), CancellationToken.None)).Estado);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, null, null, null, "activo"), CancellationToken.None));
        Assert.Equal(GestionPlanes.CodigoFinalizado, error.Codigo);
    }

    [Fact]
    public async Task Actualizar_EstadoDesconocido_O_PlanInexistente()
    {
        var plan = Existente(Hoy.AddDays(20));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.ActualizarAsync(plan.Id, new DatosActualizacionPlan(null, null, null, null, "pausado"), CancellationToken.None));
        Assert.Equal(["estado"], error.Errores.Keys);
        Assert.Equal(PlanPreventivo.Activo, plan.Estado);

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            _caso.ActualizarAsync(Guid.NewGuid(), new DatosActualizacionPlan(null, null, null, null, "suspendido"), CancellationToken.None));
    }

    [Fact]
    public async Task Listar_ValidaElEstadoYLaPagina_YMarcaLosVencidos()
    {
        var vencido = Existente(Hoy.AddDays(-1));
        _repositorio.ListarAsync(Arg.Any<FiltroPlanes>(), Arg.Any<CancellationToken>()).Returns(([Leido(vencido)], 1));

        var pagina = await _caso.ListarAsync(new FiltroPlanes(null, " ACTIVO "), CancellationToken.None);

        Assert.True(Assert.Single(pagina.Items).Vencido);
        await _repositorio.Received(1).ListarAsync(Arg.Is<FiltroPlanes>(f => f.Estado == PlanPreventivo.Activo), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroPlanes(null, "otro"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroPlanes(Tamano: 0), CancellationToken.None));
    }
}
