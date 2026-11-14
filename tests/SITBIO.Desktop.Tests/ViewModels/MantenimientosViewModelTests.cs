using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-015 Mantenimientos (HU-019): lista de planes, formulario del Ingeniero (CA-HU-019-01: el plan queda asociado
/// al equipo con su periodicidad y próxima fecha), cambios de estado y llegada desde una alerta (NAV-011).
/// Hoy = 06/11/2026 en Bogotá.
/// </summary>
public class MantenimientosViewModelTests
{
    private static readonly DateOnly Hoy = EquiposPrueba.Hoy;

    private readonly IServicioMantenimiento _servicio = Substitute.For<IServicioMantenimiento>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly List<FiltroPlanes> _consultas = [];
    private IReadOnlyList<PlanMantenimiento> _planes = [];

    public MantenimientosViewModelTests()
    {
        _servicio.ListarPlanesAsync(Arg.Any<FiltroPlanes>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _consultas.Add(llamada.ArgAt<FiltroPlanes>(0));
            return new PaginaPlanes(_planes, 1, MantenimientosViewModel.TamanoPagina, _planes.Count);
        });
        _servicio.ListarCalibracionesAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>())
            .Returns(new PaginaCalibraciones([], 1, CalibracionesViewModel.TamanoPagina, 0));
    }

    private static PlanMantenimiento Plan(
        string placa = "HSV-EQ-0412",
        DateOnly? proxima = null,
        string estado = EstadosPlan.Activo,
        DateOnly? ultima = null,
        int dias = 30) =>
        new(Guid.NewGuid(), Guid.NewGuid(), placa, "Monitor de signos vitales", "UCI adultos", "Mantenimiento preventivo", dias, ultima,
            proxima ?? Hoy.AddDays(20), estado, (proxima ?? Hoy.AddDays(20)) < Hoy, DateTimeOffset.Now);

    private async Task<MantenimientosViewModel> AbrirAsync(string rol = "AC-02", params PlanMantenimiento[] planes)
    {
        _planes = planes;
        var vm = new MantenimientosViewModel(_servicio, _equipos, EquiposPrueba.Sesion(rol), new EquiposPrueba.Reloj());
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Cargar_TraeLosActivos_ConTextosDeVencimientoYEstado()
    {
        var vencido = Plan("PL-001", Hoy.AddDays(-3));
        var hoy = Plan("PL-002", Hoy, ultima: Hoy.AddDays(-30));
        var suspendido = Plan("PL-003", Hoy.AddDays(-10), EstadosPlan.Suspendido);

        var vm = await AbrirAsync("AC-02", vencido, hoy, suspendido);

        Assert.Equal(EstadosPlan.Activo, Assert.Single(_consultas).Estado);
        Assert.Equal(("Vencido hace 3 días", "Vencido", TonoEstado.Critico), (vm.Filas[0].Vencimiento, vm.Filas[0].Estado, vm.Filas[0].Tono));
        Assert.Equal(("Vence hoy", "Activo", "Cada 30 días", "07/10/2026"), (vm.Filas[1].Vencimiento, vm.Filas[1].Estado, vm.Filas[1].Periodicidad, vm.Filas[1].Ultimo));
        Assert.Equal((string.Empty, "Suspendido", TonoEstado.Advertencia, true), (vm.Filas[2].Vencimiento, vm.Filas[2].Estado, vm.Filas[2].Tono, vm.Filas[2].PuedeReactivar));
        Assert.Equal("1–3 de 3 planes", vm.ResumenPagina);

        vm.FiltroEstado = MantenimientosViewModel.OpcionesEstado[0];
        Assert.Null(_consultas[^1].Estado);
    }

    [Fact]
    public async Task Crear_ConElUltimo_MuestraElProximoCalculado_YEnviaElPlanDelEquipo()
    {
        var equipo = EquiposPrueba.Equipo("HSV-EQ-0412");
        _equipos.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(EquiposPrueba.Pagina(1, equipo));
        var creado = Plan("HSV-EQ-0412", Hoy.AddDays(170), ultima: Hoy.AddDays(-10), dias: 180);
        _servicio.CrearPlanAsync(Arg.Any<DatosPlan>(), Arg.Any<CancellationToken>()).Returns(creado);
        var vm = await AbrirAsync();

        vm.NuevoPlanCommand.Execute(null);
        vm.Busqueda = "0412";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);
        vm.ElegirEquipoCommand.Execute(Assert.Single(vm.Coincidencias));
        vm.Descripcion = " Preventivo semestral ";
        vm.Periodicidad = "180";
        vm.UltimaEjecucion = "27/10/2026";

        Assert.Equal("Próximo preventivo: 25/04/2027.", vm.ProximoCalculado);
        await vm.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CrearPlanAsync(new DatosPlan(equipo.Id, "Preventivo semestral", 180, new DateOnly(2026, 10, 27), null), Arg.Any<CancellationToken>());
        Assert.False(vm.PanelAbierto);
        Assert.Equal("Plan de HSV-EQ-0412 creado. Próximo preventivo: 25/04/2027.", vm.Exito);
        Assert.Equal(2, _consultas.Count);
    }

    [Fact]
    public async Task Crear_ConElUltimoAtrasado_AvisaQueNaceVencido()
    {
        var vm = await AbrirAsync();
        vm.NuevoPlanCommand.Execute(null);
        vm.Periodicidad = "30";
        vm.UltimaEjecucion = "01/09/2026";

        Assert.StartsWith("Próximo preventivo: 01/10/2026. Ya pasó", vm.ProximoCalculado);
    }

    [Theory]
    [InlineData("", "", "", nameof(MantenimientosViewModel.ErrorPeriodicidad), MantenimientosViewModel.PeriodicidadInvalida)]
    [InlineData("30", "", "", nameof(MantenimientosViewModel.ErrorProxima), MantenimientosViewModel.SinFechas)]
    [InlineData("30", "10/11/2026", "", nameof(MantenimientosViewModel.ErrorUltima), MantenimientosViewModel.UltimaFutura)]
    [InlineData("30", "", "01/11/2026", nameof(MantenimientosViewModel.ErrorProxima), MantenimientosViewModel.ProximaPasada)]
    [InlineData("30", "01/11/2026", "01/10/2026", nameof(MantenimientosViewModel.ErrorProxima), MantenimientosViewModel.ProximaAntesDeUltima)]
    [InlineData("30", "", "31/02/2026", nameof(MantenimientosViewModel.ErrorProxima), MantenimientosViewModel.FechaInvalida)]
    public async Task Crear_DatosInvalidos_ExplicaJuntoAlCampo_YNoLlamaALaApi(string dias, string ultima, string proxima, string campo, string mensaje)
    {
        var vm = await AbrirAsync();
        vm.NuevoPlanCommand.Execute(null);
        vm.Periodicidad = dias;
        vm.UltimaEjecucion = ultima;
        vm.ProximaFecha = proxima;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(MantenimientosViewModel.SinEquipo, vm.ErrorEquipo);
        Assert.Equal(mensaje, typeof(MantenimientosViewModel).GetProperty(campo)!.GetValue(vm));
        Assert.True(vm.PanelAbierto);
        await _servicio.DidNotReceiveWithAnyArgs().CrearPlanAsync(default!, default);
    }

    [Fact]
    public async Task Crear_ErrorDeLaApi_SeMuestraJuntoAlCampoOEnElPanel()
    {
        var equipo = EquiposPrueba.Equipo();
        var vm = await AbrirAsync();
        vm.NuevoPlanCommand.Execute(null);
        vm.ElegirEquipoCommand.Execute(equipo);
        vm.Periodicidad = "30";
        vm.ProximaFecha = "01/12/2026";

        _servicio.CrearPlanAsync(Arg.Any<DatosPlan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Datos inválidos", new Dictionary<string, string> { ["periodicidadDias"] = "Muy larga." }));
        await vm.GuardarCommand.ExecuteAsync(null);
        Assert.Equal(("Muy larga.", (Aviso?)null), (vm.ErrorPeriodicidad, vm.AvisoPanel));

        _servicio.CrearPlanAsync(Arg.Any<DatosPlan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("PLN-001", "El equipo está dado de baja.", new Dictionary<string, string>()));
        await vm.GuardarCommand.ExecuteAsync(null);
        Assert.Equal(("PLN-001", "El equipo está dado de baja."), (vm.AvisoPanel!.Codigo, vm.AvisoPanel.Mensaje));
        Assert.True(vm.PanelAbierto);
    }

    [Fact]
    public async Task Editar_CargaElPlan_YEnviaLosCambios()
    {
        var plan = Plan("PL-001", Hoy.AddDays(20), ultima: Hoy.AddDays(-10));
        var vm = await AbrirAsync("AC-02", plan);
        _servicio.ActualizarPlanAsync(plan.Id, Arg.Any<DatosActualizacionPlan>(), Arg.Any<CancellationToken>())
            .Returns(plan with { PeriodicidadDias = 60, ProximaFecha = Hoy.AddDays(50) });

        vm.AbrirEdicionCommand.Execute(vm.Filas[0]);
        Assert.Equal(("Plan de PL-001", false, "30", "27/10/2026", "26/11/2026"), (vm.TituloPanel, vm.EsNuevo, vm.Periodicidad, vm.UltimaEjecucion, vm.ProximaFecha));

        // Vaciar el próximo: la API lo recalcula desde el último.
        vm.Periodicidad = "60";
        vm.ProximaFecha = string.Empty;
        await vm.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).ActualizarPlanAsync(
            plan.Id, new DatosActualizacionPlan("Mantenimiento preventivo", 60, Hoy.AddDays(-10), null), Arg.Any<CancellationToken>());
        Assert.Equal(("Cada 60 días", "26/12/2026"), (vm.Filas[0].Periodicidad, vm.Filas[0].Proximo));
        Assert.False(vm.PanelAbierto);
    }

    [Fact]
    public async Task Editar_UnPlanVencidoSinUltimo_ConservaSuProximaFecha()
    {
        var plan = Plan("PL-001", Hoy.AddDays(-3));
        var vm = await AbrirAsync("AC-02", plan);
        _servicio.ActualizarPlanAsync(plan.Id, Arg.Any<DatosActualizacionPlan>(), Arg.Any<CancellationToken>()).Returns(plan);

        vm.AbrirEdicionCommand.Execute(vm.Filas[0]);
        vm.Periodicidad = "45";
        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorProxima);
        await _servicio.Received(1).ActualizarPlanAsync(plan.Id, new DatosActualizacionPlan("Mantenimiento preventivo", 45, null, Hoy.AddDays(-3)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspender_YReactivar_CambianLaFila_YFinalizarPideConfirmar()
    {
        var plan = Plan("PL-001");
        var vm = await AbrirAsync("AC-02", plan);
        _servicio.ActualizarPlanAsync(plan.Id, Arg.Is<DatosActualizacionPlan>(d => d.Estado == EstadosPlan.Suspendido), Arg.Any<CancellationToken>())
            .Returns(plan with { Estado = EstadosPlan.Suspendido });
        _servicio.ActualizarPlanAsync(plan.Id, Arg.Is<DatosActualizacionPlan>(d => d.Estado == EstadosPlan.Finalizado), Arg.Any<CancellationToken>())
            .Returns(plan with { Estado = EstadosPlan.Finalizado });

        await vm.CambiarEstadoCommand.ExecuteAsync(vm.Filas[0].Suspender);
        Assert.Equal(("Suspendido", true), (vm.Filas[0].Estado, vm.Filas[0].PuedeReactivar));
        Assert.Contains("suspendido", vm.Exito, StringComparison.Ordinal);

        // Finalizar no se deshace: primero pide confirmar.
        await vm.CambiarEstadoCommand.ExecuteAsync(vm.Filas[0].Finalizar);
        Assert.Equal(vm.Filas[0], vm.ConfirmandoFinalizar);
        await _servicio.DidNotReceive().ActualizarPlanAsync(plan.Id, Arg.Is<DatosActualizacionPlan>(d => d.Estado == EstadosPlan.Finalizado), Arg.Any<CancellationToken>());

        await vm.CambiarEstadoCommand.ExecuteAsync(vm.ConfirmandoFinalizar!.Finalizar);
        Assert.Null(vm.ConfirmandoFinalizar);
        Assert.Equal(("Finalizado", false), (vm.Filas[0].Estado, vm.Filas[0].Editable));
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-03")]
    [InlineData("AC-05")]
    [InlineData("AC-07")]
    public async Task OtrosRoles_SoloConsultan(string rol)
    {
        var vm = await AbrirAsync(rol, Plan("PL-001"));

        vm.NuevoPlanCommand.Execute(null);
        vm.AbrirEdicionCommand.Execute(vm.Filas[0]);
        await vm.CambiarEstadoCommand.ExecuteAsync(vm.Filas[0].Suspender);

        Assert.Equal((false, true, false), (vm.PuedeGestionar, vm.SoloConsulta, vm.PanelAbierto));
        Assert.Single(vm.Filas);
        await _servicio.DidNotReceiveWithAnyArgs().ActualizarPlanAsync(default, default!, default);
    }

    [Fact]
    public async Task DesdeUnaAlerta_LimitaLaListaAlEquipo_YElIngenieroAbreElPlan()
    {
        var plan = Plan("PL-009", Hoy.AddDays(-2));
        var vm = await AbrirAsync("AC-02", Plan("PL-001"));
        _servicio.ObtenerPlanAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _planes = [plan];

        vm.Recibir($"{MantenimientosViewModel.PrefijoPlan}{plan.Id}");

        Assert.Equal((plan.EquipoId, (string?)null), (_consultas[^1].EquipoId, _consultas[^1].Estado));
        Assert.Equal(("Solo el equipo PL-009", true, "Plan de PL-009"), (vm.TextoFiltroEquipo, vm.PanelAbierto, vm.TituloPanel));

        await vm.QuitarFiltroEquipoCommand.ExecuteAsync(null);
        Assert.Null(_consultas[^1].EquipoId);
    }

    [Fact]
    public async Task DesdeUnaAlertaDeCalibracion_AbreLaPestanaDeCalibraciones_ConElHistorialDelEquipo()
    {
        var vm = await AbrirAsync("AC-03");
        var equipo = Guid.NewGuid();

        vm.Recibir($"{MantenimientosViewModel.PrefijoCalibracion}{equipo}");

        Assert.Equal((true, false, false), (vm.EnCalibraciones, vm.EnPlanes, vm.PuedeCrearPlan));
        await _servicio.Received(1).ListarCalibracionesAsync(Arg.Is<FiltroCalibraciones>(f => f.EquipoId == equipo), Arg.Any<CancellationToken>());
        Assert.True(vm.Calibraciones.HayFiltroEquipo);
        vm.MostrarPlanesCommand.Execute(null);
        Assert.True(vm.EnPlanes);
    }

    [Fact]
    public async Task SinConexion_MuestraElAvisoConReintento()
    {
        _servicio.ListarPlanesAsync(Arg.Any<FiltroPlanes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Sin conexión con el servidor.", new Dictionary<string, string>(), sinConexion: true));
        var vm = new MantenimientosViewModel(_servicio, _equipos, EquiposPrueba.Sesion("AC-02"), new EquiposPrueba.Reloj());

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("MSG-011", true, false), (vm.Aviso!.Codigo, vm.Aviso.PuedeReintentar, vm.SinPlanes));
    }
}
