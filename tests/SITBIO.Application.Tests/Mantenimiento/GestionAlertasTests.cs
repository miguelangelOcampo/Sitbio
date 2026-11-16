using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Mantenimiento;

/// <summary>
/// Alertas de vencimiento (HU-020) y generación de órdenes preventivas (CA-HU-019-02): al consultar se
/// generan las órdenes de los planes cuya fecha llegó, repartidas entre los técnicos menos ocupados; las
/// alertas usan los días de anticipación de la institución y los vencidos van primero.
/// </summary>
public class GestionAlertasTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 13);

    private readonly IRepositorioPlanes _repositorio = Substitute.For<IRepositorioPlanes>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly GestionAlertas _caso;
    private IReadOnlyList<PlanPreventivo> _pendientes = [];
    private IReadOnlyList<TecnicoConCarga> _tecnicos = [];

    public GestionAlertasTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_usuario);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _repositorio.DiasAnticipacionAsync(Arg.Any<CancellationToken>()).Returns(15);
        _repositorio.PlanesPorVencerAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repositorio.CalibracionesPorVencerAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repositorio.GenerarOrdenesAsync(
                Arg.Any<DateOnly>(),
                Arg.Any<Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>>>(),
                Arg.Any<CancellationToken>())
            .Returns(llamada => llamada.Arg<Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>>>()(_pendientes, _tecnicos));
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionAlertas(_repositorio, _auditoria, _contexto, tiempo);
    }

    private static PlanPreventivo Plan(DateOnly proxima, string descripcion = "Preventivo mensual") =>
        new(Tenant, Guid.NewGuid(), descripcion, 30, proxima, Ahora.AddDays(-90));

    private static PlanLeido Leido(PlanPreventivo plan, string placa = "PL-001") => new(plan, placa, "Bomba de infusión", "UCI");

    [Fact]
    public async Task Consultar_GeneraLasOrdenesDeLosPlanesQueLlegaron_AlTecnicoMenosOcupado_YLasAudita()
    {
        var ana = new TecnicoConCarga(Guid.NewGuid(), "Ana", 0);
        var beto = new TecnicoConCarga(Guid.NewGuid(), "Beto", 1);
        _tecnicos = [ana, beto];
        _pendientes = [Plan(Hoy), Plan(Hoy.AddDays(-3)), Plan(Hoy.AddDays(-1))];

        var respuesta = await _caso.ConsultarAsync(CancellationToken.None);

        Assert.Equal(3, respuesta.OrdenesGeneradas);
        await _repositorio.Received(1).GenerarOrdenesAsync(Hoy, Arg.Any<Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>>>(), Arg.Any<CancellationToken>());
        await _auditoria.Received(3).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Crear,
            GestionOrdenes.Entidad,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["tipo"] == TiposMantenimiento.Preventivo && d.ContainsKey("plan")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Repartir_DelMasAtrasadoAlMasReciente_SiempreAlMenosOcupado()
    {
        var ana = new TecnicoConCarga(Guid.NewGuid(), "Ana", 0);
        var beto = new TecnicoConCarga(Guid.NewGuid(), "Beto", 1);
        var reciente = Plan(Hoy);
        var atrasado = Plan(Hoy.AddDays(-3));
        var intermedio = Plan(Hoy.AddDays(-1));

        var ordenes = GestionAlertas.Repartir([reciente, atrasado, intermedio], [ana, beto], Ahora);

        Assert.Equal([atrasado.Id, intermedio.Id, reciente.Id], ordenes.Select(o => o.PlanId!.Value));
        Assert.Equal([ana.Id, ana.Id, beto.Id], ordenes.Select(o => o.TecnicoId));
        Assert.All(ordenes, o => Assert.Equal((TiposMantenimiento.Preventivo, EstadosOrden.Asignada, (Guid?)null), (o.TipoMantenimiento, o.Estado, o.AsignadoPorId)));
        Assert.Equal((DateOnly?)atrasado.ProximaFecha, ordenes[0].FechaProgramada);
    }

    [Fact]
    public void Repartir_SinTecnicosActivos_NoGeneraNada()
    {
        Assert.Empty(GestionAlertas.Repartir([Plan(Hoy)], [], Ahora));
    }

    [Fact]
    public async Task Consultar_UsaLosDiasDeAnticipacion_YMarcaVencidasYProximas_LasVencidasPrimero()
    {
        _repositorio.DiasAnticipacionAsync(Arg.Any<CancellationToken>()).Returns(20);
        var vencido = Plan(Hoy.AddDays(-4), "Preventivo trimestral");
        var proximo = Plan(Hoy.AddDays(10));
        var ordenId = Guid.NewGuid();
        _repositorio.PlanesPorVencerAsync(Hoy.AddDays(20), Arg.Any<CancellationToken>()).Returns(
        [
            new PlanPorVencer(Leido(proximo, "PL-002"), null, null),
            new PlanPorVencer(Leido(vencido), ordenId, EstadosOrden.Asignada),
        ]);
        var monitor = Guid.NewGuid();
        _repositorio.CalibracionesPorVencerAsync(Hoy.AddDays(20), Arg.Any<CancellationToken>()).Returns(
        [
            new CalibracionPorVencer(monitor, "MON-07", "Monitor", "Urgencias", Hoy),
        ]);

        var respuesta = await _caso.ConsultarAsync(CancellationToken.None);

        Assert.Equal((20, 0), (respuesta.DiasAnticipacion, respuesta.OrdenesGeneradas));
        Assert.Equal(["PL-001", "MON-07", "PL-002"], respuesta.Alertas.Select(a => a.PlacaInventario));

        var primera = respuesta.Alertas[0];
        Assert.Equal((TiposAlerta.Preventivo, EstadosAlerta.Vencida, -4), (primera.Tipo, primera.Estado, primera.DiasRestantes));
        Assert.Equal(((Guid?)vencido.Id, (Guid?)ordenId, EstadosOrden.Asignada), (primera.PlanId, primera.OrdenId, primera.EstadoOrden));
        Assert.Equal("Preventivo trimestral", primera.Descripcion);

        // Vence hoy: todavía es «próxima».
        var calibracion = respuesta.Alertas[1];
        Assert.Equal((TiposAlerta.Calibracion, EstadosAlerta.Proxima, 0, monitor), (calibracion.Tipo, calibracion.Estado, calibracion.DiasRestantes, calibracion.EquipoId));
        Assert.Null(calibracion.PlanId);

        Assert.Equal((EstadosAlerta.Proxima, 10, (Guid?)null), (respuesta.Alertas[2].Estado, respuesta.Alertas[2].DiasRestantes, respuesta.Alertas[2].OrdenId));
    }

    [Fact]
    public async Task Consultar_SinNadaPorVencer_NoTraeAlertasNiAudita()
    {
        var respuesta = await _caso.ConsultarAsync(CancellationToken.None);

        Assert.Empty(respuesta.Alertas);
        Assert.Equal(15, respuesta.DiasAnticipacion);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }
}
