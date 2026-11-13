using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Ordenes;

/// <summary>
/// Aprobación y firma de la orden por el Ingeniero Biomédico (HU-021; RF-021, RN-004, RN-007): la
/// intervención se firma en MongoDB, la orden queda aprobada con la firma, el tiquete se cierra y el
/// equipo toma su estado de salida. Si MongoDB falla, nada cambia.
/// </summary>
public class GestionAprobacionTests
{
    private const string ObjectId = "65f1a2b3c4d5e6f7a8b9c0d1";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioOrdenes _ordenes = Substitute.For<IRepositorioOrdenes>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Guid _ingeniero = Guid.NewGuid();
    private readonly Tiquete _tiquete;
    private readonly OrdenTrabajo _orden;
    private readonly EquipoBiomedico _equipo;
    private readonly GestionAprobacion _caso;

    public GestionAprobacionTests()
    {
        _tiquete = new Tiquete(Tenant, Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, Criticidades.Critica, Ahora.AddHours(-5));
        _tiquete.Numerar(9);
        (_orden, _) = OrdenTrabajo.Correctiva(_tiquete, _tecnico, _ingeniero, null, null, Ahora.AddHours(-4));
        _orden.Iniciar(Ahora.AddHours(-3));
        _tiquete.IniciarEjecucion(_tecnico, "Inició", Ahora.AddHours(-3));
        _orden.EnlazarIntervencion(ObjectId);
        _orden.Cerrar(Ahora.AddHours(-1));

        // RN-005: la falla crítica dejó el equipo «Fuera de servicio».
        _equipo = new EquipoBiomedico(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SN-1", "PL-001", "Baxter", "Sigma", Ahora.AddYears(-1));
        _equipo.CambiarEstado(EstadosEquipo.FueraDeServicio, "Falla crítica", Guid.NewGuid(), Ahora.AddHours(-5));

        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_ingeniero);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _ordenes.OrdenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OrdenTrabajo?)null);
        _ordenes.OrdenAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(_orden);
        _ordenes.ObtenerAsync(_orden.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new OrdenLeida(_orden, "PL-001", "SN-1", "Bomba de infusión", "UCI", "Carlos Ruiz", _tiquete.Codigo, _tiquete.TipoFalla));
        _ordenes.TiqueteAsync(_tiquete.Id, Arg.Any<CancellationToken>()).Returns(_tiquete);
        Salida(EstadosSalidaEquipo.Operativo);
        Calibracion(new DateOnly(2027, 5, 1));

        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionAprobacion(_ordenes, _intervenciones, _auditoria, _contexto, tiempo);
    }

    private void Salida(string salida) =>
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectId, new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _tecnico, TiposMantenimiento.Correctivo, "Sensor descalibrado", "Calibración", [], null, 0m, 30, salida,
            new Dictionary<string, object>(), 2, Ahora.AddHours(-2), false)));

    private void Calibracion(DateOnly? vence) =>
        _ordenes.EquipoAsync(_orden.EquipoId, Arg.Any<CancellationToken>()).Returns(new EquipoDeLaOrden(_equipo, "Bomba de infusión", "UCI", vence));

    private Task<OrdenDto> AprobarAsync(string? firma = "Laura Gómez") =>
        _caso.AprobarAsync(_orden.Id, new DatosAprobacion(firma), CancellationToken.None);

    [Fact]
    public async Task Aprobar_FirmaLaIntervencion_LaOrdenQuedaAprobada_ElTiqueteCerrado_YElEquipoConSuEstadoDeSalida()
    {
        var dto = await AprobarAsync("  Laura Gómez  ");

        Assert.Equal((EstadosOrden.Aprobada, "Laura Gómez"), (dto.Estado, dto.FirmaAprobacion));
        Assert.Equal(((Guid?)_ingeniero, (DateTimeOffset?)Ahora), (dto.AprobadoPorId, dto.FechaAprobacion));
        Assert.Equal((EstadosTiquete.Cerrado, (DateTimeOffset?)Ahora), (_tiquete.Estado, _tiquete.FechaCierre));
        Assert.Equal(EstadosEquipo.Operativo, _equipo.EstadoEquipoId);

        Received.InOrder(() =>
        {
            _intervenciones.FirmarAsync(
                ObjectId,
                EstadosSalidaEquipo.Operativo,
                Arg.Is<FotoEquipo>(f => f.Nombre == "Bomba de infusión PL-001" && f.Marca == "Baxter" && f.Modelo == "Sigma"
                    && f.TipoEquipo == "Bomba de infusión" && f.Ubicacion == "UCI" && f.CapturadoEn == Ahora),
                Arg.Any<CancellationToken>());
            _ordenes.GuardarAprobacionAsync(
                Arg.Is<HistorialTiquete?>(h => h != null && h.EstadoAnterior == EstadosTiquete.EnEjecucion && h.NuevoEstado == EstadosTiquete.Cerrado && h.UsuarioId == _ingeniero),
                Arg.Is<HistorialEstadoEquipo?>(h => h != null && h.EstadoAnteriorId == EstadosEquipo.FueraDeServicio && h.NuevoEstadoId == EstadosEquipo.Operativo && h.UsuarioId == _ingeniero),
                Arg.Any<CancellationToken>());
        });
        await _auditoria.Received(1).RegistrarAsync(
            _ingeniero,
            AccionesAuditoria.CierreOt,
            GestionOrdenes.Entidad,
            _orden.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["intervencion"] == ObjectId && d["estado_salida"] == EstadosSalidaEquipo.Operativo),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprobar_EquipoYaEnSuEstadoDeSalida_NoCambiaElEquipo()
    {
        Salida(EstadosSalidaEquipo.FueraDeServicio);

        await AprobarAsync();

        await _ordenes.Received(1).GuardarAprobacionAsync(Arg.Is<HistorialTiquete?>(h => h != null), null, Arg.Any<CancellationToken>());
        Assert.Equal(EstadosEquipo.FueraDeServicio, _equipo.EstadoEquipoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("x")]
    public async Task Aprobar_SinFirmaOMuyLarga_Responde400SinTocarNada(string? firma)
    {
        var valor = firma == "x" ? new string('x', OrdenTrabajo.LongitudMaximaFirma + 1) : firma;

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => AprobarAsync(valor));

        Assert.Equal(["firma"], error.Errores.Keys);
        Assert.Equal(EstadosOrden.Cerrada, _orden.Estado);
        await _intervenciones.DidNotReceiveWithAnyArgs().FirmarAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Aprobar_OrdenQueNoEstaCerrada_Responde409_YLaYaAprobadaRn004()
    {
        await AprobarAsync();
        var otraVez = await Assert.ThrowsAsync<ErrorConflicto>(() => AprobarAsync("Otra firma"));
        Assert.Equal(GestionEjecucion.CodigoFirmada, otraVez.Codigo);
        Assert.Equal("Laura Gómez", _orden.FirmaAprobacion);

        var (abierta, _) = OrdenTrabajo.Correctiva(
            new Tiquete(Tenant, Guid.NewGuid(), Guid.NewGuid(), "Ruido", null, Criticidades.Baja, Ahora), _tecnico, _ingeniero, null, null, Ahora);
        abierta.Iniciar(Ahora);
        _ordenes.OrdenAsync(abierta.Id, Arg.Any<CancellationToken>()).Returns(abierta);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.AprobarAsync(abierta.Id, new DatosAprobacion("Laura Gómez"), CancellationToken.None));

        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, error.Codigo);
        Assert.Equal(EstadosOrden.EnEjecucion, abierta.Estado);
        await _intervenciones.Received(1).FirmarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FotoEquipo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprobar_ComoOperativoConLaCalibracionVencida_SeImpideConRn007()
    {
        Calibracion(new DateOnly(2026, 11, 12));

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => AprobarAsync());

        Assert.Equal(ReglaCalibracion.Codigo, error.Codigo);
        Assert.Contains("12/11/2026", error.Message);
        Assert.Equal(EstadosOrden.Cerrada, _orden.Estado);
        await _intervenciones.DidNotReceiveWithAnyArgs().FirmarAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Aprobar_SiMongoNoResponde_LaOrdenSigueCerrada_YNoSeGuardaNada()
    {
        _intervenciones.FirmarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FotoEquipo>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorServicioNoDisponible(new TimeoutException()));

        await Assert.ThrowsAsync<ErrorServicioNoDisponible>(() => AprobarAsync());

        Assert.Equal((EstadosOrden.Cerrada, (string?)null), (_orden.Estado, _orden.FirmaAprobacion));
        Assert.Equal((EstadosTiquete.EnEjecucion, EstadosEquipo.FueraDeServicio), (_tiquete.Estado, _equipo.EstadoEquipoId));
        await _ordenes.DidNotReceiveWithAnyArgs().GuardarAprobacionAsync(default, default, default);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Aprobar_OrdenInexistenteODeOtraInstitucion_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.AprobarAsync(Guid.NewGuid(), new DatosAprobacion("Laura Gómez"), CancellationToken.None));
    }

    [Fact]
    public async Task Aprobar_Preventiva_ElPlanAvanza_UltimoHoyYProximoMasLaPeriodicidad()
    {
        var plan = new PlanPreventivo(Tenant, _equipo.Id, "Preventivo trimestral", 90, new DateOnly(2026, 11, 10), Ahora.AddDays(-100));
        var orden = OrdenTrabajo.Preventiva(plan, _tecnico, Ahora.AddDays(-3));
        orden.Iniciar(Ahora.AddHours(-3));
        orden.EnlazarIntervencion(ObjectId);
        orden.Cerrar(Ahora.AddHours(-1));
        _ordenes.OrdenAsync(orden.Id, Arg.Any<CancellationToken>()).Returns(orden);
        _ordenes.PlanAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _ordenes.EquipoAsync(orden.EquipoId, Arg.Any<CancellationToken>()).Returns(new EquipoDeLaOrden(_equipo, "Bomba de infusión", "UCI", new DateOnly(2027, 5, 1)));
        _ordenes.ObtenerAsync(orden.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new OrdenLeida(orden, "PL-001", "SN-1", "Bomba de infusión", "UCI", "Carlos Ruiz", null, null));
        _intervenciones.ObtenerAsync(orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectId, new Intervencion(
            Guid.NewGuid(), orden.Id, orden.EquipoId, _tecnico, TiposMantenimiento.Preventivo, "Revisión", "Limpieza y pruebas", [], null, 0m, 60,
            EstadosSalidaEquipo.FueraDeServicio, new Dictionary<string, object>(), 2, Ahora.AddHours(-2), false)));

        var dto = await _caso.AprobarAsync(orden.Id, new DatosAprobacion("Laura Gómez"), CancellationToken.None);

        // Ahora es el 13/11/2026 a las 10:00 en Colombia.
        Assert.Equal((EstadosOrden.Aprobada, (Guid?)plan.Id), (dto.Estado, dto.PlanId));
        Assert.Equal(((DateOnly?)new DateOnly(2026, 11, 13), new DateOnly(2027, 2, 11)), (plan.UltimaEjecucion, plan.ProximaFecha));
        await _ordenes.DidNotReceiveWithAnyArgs().TiqueteAsync(default, default);
        await _ordenes.Received(1).GuardarAprobacionAsync(null, null, Arg.Any<CancellationToken>());
    }
}
