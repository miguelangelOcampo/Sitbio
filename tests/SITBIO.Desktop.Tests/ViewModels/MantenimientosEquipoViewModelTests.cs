using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>Pestaña «Mantenimientos» de la hoja de vida UI-006: plan vigente, órdenes y calibraciones del equipo.</summary>
public class MantenimientosEquipoViewModelTests
{
    private static readonly Guid EquipoId = Guid.NewGuid();

    private readonly IServicioMantenimiento _mantenimiento = Substitute.For<IServicioMantenimiento>();
    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();

    public MantenimientosEquipoViewModelTests()
    {
        Planes();
        Ordenes(0);
        Calibraciones();
    }

    private void Planes(params PlanMantenimiento[] planes) =>
        _mantenimiento.ListarPlanesAsync(default!, default).ReturnsForAnyArgs(new PaginaPlanes(planes, 1, 5, planes.Length));

    private void Ordenes(int total, params Orden[] ordenes) =>
        _ordenes.ListarAsync(default!, default).ReturnsForAnyArgs(new PaginaOrdenes(ordenes, 1, MantenimientosEquipoViewModel.Limite, total));

    private void Calibraciones(params Calibracion[] calibraciones) =>
        _mantenimiento.ListarCalibracionesAsync(default!, default).ReturnsForAnyArgs(new PaginaCalibraciones(calibraciones, 1, MantenimientosEquipoViewModel.Limite, calibraciones.Length));

    private async Task<MantenimientosEquipoViewModel> CargarAsync(string rol = "AC-02", bool requiereCalibracion = true)
    {
        var vm = new MantenimientosEquipoViewModel(_mantenimiento, _ordenes, new Reloj(), EquipoId, requiereCalibracion, rol);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private static PlanMantenimiento PlanVigente(DateOnly proxima, DateOnly? ultima = null, bool vencido = false) =>
        new(Guid.NewGuid(), EquipoId, "HSV-EQ-0412", "Monitor de signos vitales", "UCI adultos", "Inspección y prueba de alarmas",
            90, ultima, proxima, EstadosPlan.Activo, vencido, DateTimeOffset.UtcNow);

    private static Orden OrdenDe(string tipo, string estado, string? tiquete, DateTimeOffset creada) =>
        new(Guid.NewGuid(), tiquete is null ? null : Guid.NewGuid(), tiquete, Guid.NewGuid(), "Carlos Ruiz", tipo, "alta", estado, null,
            EquipoId, "HSV-EQ-0412", "Monitor de signos vitales", "UCI adultos", tiquete is null ? null : "No enciende", FechaCreacion: creada);

    private static Calibracion CalibracionDe(DateOnly fecha, DateOnly vence, bool vigente, bool aprobada = true) =>
        new(Guid.NewGuid(), EquipoId, "HSV-EQ-0412", "Monitor de signos vitales", "UCI adultos", null, "Metrología Andina",
            fecha, "Simulador Fluke ProSim 8", ResultadosCalibracion.Conforme, "CAL-2026-118", vence, vigente,
            Guid.NewGuid(), "Carlos Ruiz", aprobada ? Guid.NewGuid() : null, aprobada ? "Ana Mora" : null, aprobada);

    private static ErrorApi Error(string mensaje, bool sinConexion = false) =>
        new(sinConexion ? null : "API-500", mensaje, new Dictionary<string, string>(), sinConexion: sinConexion);

    // ---------- Plan preventivo ----------

    [Fact]
    public async Task Plan_MuestraPeriodicidadUltimaYProximaDelPlanActivoDelEquipo()
    {
        Planes(PlanVigente(Hoy.AddDays(4), ultima: new DateOnly(2026, 8, 12)));

        var vm = await CargarAsync();

        await _mantenimiento.Received(1).ListarPlanesAsync(new FiltroPlanes(EquipoId, EstadosPlan.Activo, 1, 5), Arg.Any<CancellationToken>());
        Assert.True(vm.HayPlan);
        Assert.Equal("Trimestral", vm.Periodicidad);
        Assert.Equal("12/08/2026", vm.UltimaEjecucion);
        Assert.Equal("10/11/2026", vm.ProximaFecha);
        Assert.Equal("en 4 días", vm.DetalleProxima);
        Assert.Equal("Inspección y prueba de alarmas", vm.DescripcionPlan);
        Assert.False(vm.PlanVencido);
        Assert.False(vm.SinPlan);
    }

    [Fact]
    public async Task Plan_Vencido_SeMarcaYSinEjecucionesLoDice()
    {
        Planes(PlanVigente(Hoy.AddDays(-2), vencido: true));

        var vm = await CargarAsync();

        Assert.True(vm.PlanVencido);
        Assert.Equal("Vencido · hace 2 días", vm.DetalleProxima);
        Assert.Equal("Sin ejecuciones", vm.UltimaEjecucion);
    }

    [Fact]
    public async Task SinPlanActivo_LoDiceSinInventarFechas()
    {
        var vm = await CargarAsync();

        Assert.True(vm.SinPlan);
        Assert.False(vm.HayPlan);
        Assert.Equal("—", vm.ProximaFecha);
    }

    // ---------- Órdenes ----------

    [Fact]
    public async Task Ordenes_PideLasDelEquipoYMuestraFechaTipoOrigenYEstado()
    {
        var correctiva = OrdenDe("correctivo", EstadosOrden.Aprobada, "TQT-000031", new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
        var preventiva = OrdenDe("preventivo", EstadosOrden.EnEjecucion, null, new DateTimeOffset(2026, 8, 12, 15, 0, 0, TimeSpan.Zero));
        Ordenes(2, correctiva, preventiva);

        var vm = await CargarAsync();

        await _ordenes.Received(1).ListarAsync(new FiltroOrdenes(Tamano: MantenimientosEquipoViewModel.Limite, EquipoId: EquipoId), Arg.Any<CancellationToken>());
        Assert.Equal("2 órdenes", vm.ResumenOrdenes);
        var (primera, segunda) = (vm.OrdenesEquipo![0], vm.OrdenesEquipo[1]);
        Assert.Equal(("20/10/2026", "Correctivo", "TQT-000031", "No enciende", "Aprobada", TonoEstado.Exito), (primera.Fecha, primera.Tipo, primera.Origen, primera.Detalle, primera.Estado, primera.Tono));
        Assert.Equal(("Preventivo", "Plan preventivo", "Mantenimiento programado", "En ejecución", TonoEstado.Advertencia), (segunda.Tipo, segunda.Origen, segunda.Detalle, segunda.Estado, segunda.Tono));
        Assert.False(vm.SinOrdenes);
    }

    [Fact]
    public async Task Ordenes_MasDeLasQueSeTraen_AvisaQueSonLasMasRecientes()
    {
        Ordenes(64, OrdenDe("correctivo", EstadosOrden.Cerrada, "TQT-000002", DateTimeOffset.UtcNow), OrdenDe("correctivo", EstadosOrden.Cerrada, "TQT-000001", DateTimeOffset.UtcNow.AddDays(-1)));

        var vm = await CargarAsync();

        Assert.Equal("Las 2 más recientes de 64", vm.ResumenOrdenes);
    }

    [Fact]
    public async Task SinOrdenes_LoDice()
    {
        var vm = await CargarAsync();

        Assert.True(vm.SinOrdenes);
        Assert.Equal(string.Empty, vm.ResumenOrdenes);
    }

    [Fact]
    public async Task GerenteBiomedico_NoConsultaLasOrdenes()
    {
        var vm = await CargarAsync("AC-05");

        Assert.False(vm.PuedeVerOrdenes);
        Assert.False(vm.SinOrdenes);
        await _ordenes.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
        await _mantenimiento.ReceivedWithAnyArgs(1).ListarPlanesAsync(default!, default);
    }

    [Fact]
    public async Task Tecnico_VeSoloSusOrdenes()
    {
        var vm = await CargarAsync("AC-03");

        Assert.True(vm.PuedeVerOrdenes);
        Assert.True(vm.SoloSuyas);
    }

    // ---------- Calibraciones ----------

    [Fact]
    public async Task Calibracion_VigenteSegunLaMasReciente()
    {
        Calibraciones(
            CalibracionDe(new DateOnly(2026, 3, 2), new DateOnly(2027, 3, 2), vigente: true),
            CalibracionDe(new DateOnly(2025, 3, 1), new DateOnly(2026, 3, 1), vigente: false));

        var vm = await CargarAsync();

        await _mantenimiento.Received(1).ListarCalibracionesAsync(new FiltroCalibraciones(EquipoId, 1, MantenimientosEquipoViewModel.Limite), Arg.Any<CancellationToken>());
        Assert.Equal("Vigente", vm.Vigencia);
        Assert.Equal("Hasta 02/03/2027", vm.DetalleVigencia);
        Assert.Equal(TonoEstado.Exito, vm.TonoVigencia);
        Assert.Equal(2, vm.Calibraciones!.Count);
        Assert.False(vm.Calibraciones[0].PuedeCorregir);
        Assert.False(vm.Calibraciones[0].PuedeAprobar);
    }

    [Fact]
    public async Task Calibracion_Vencida_SeMarcaEnRojo()
    {
        Calibraciones(CalibracionDe(new DateOnly(2025, 10, 1), new DateOnly(2026, 10, 1), vigente: false));

        var vm = await CargarAsync();

        Assert.Equal("Vencida", vm.Vigencia);
        Assert.Equal("Venció el 01/10/2026", vm.DetalleVigencia);
        Assert.Equal(TonoEstado.Critico, vm.TonoVigencia);
    }

    [Fact]
    public async Task Calibracion_SinRegistros_LoDice()
    {
        var vm = await CargarAsync();

        Assert.True(vm.SinCalibraciones);
        Assert.Equal("Sin calibraciones", vm.Vigencia);
        Assert.Equal(TonoEstado.Advertencia, vm.TonoVigencia);
    }

    [Fact]
    public async Task RN010_EquipoQueNoRequiereCalibracion_NoAplicaYNoConsulta()
    {
        var vm = await CargarAsync(requiereCalibracion: false);

        Assert.Equal("No aplica", vm.Vigencia);
        Assert.Equal("El equipo no requiere calibración (RN-010)", vm.DetalleVigencia);
        Assert.False(vm.SinCalibraciones);
        await _mantenimiento.DidNotReceiveWithAnyArgs().ListarCalibracionesAsync(default!, default);
    }

    // ---------- Errores ----------

    [Fact]
    public async Task ErrorEnUnaSeccion_NoTumbaLasDemas_YReintentarVuelveAConsultar()
    {
        Planes(PlanVigente(Hoy.AddDays(4)));
        _ordenes.ListarAsync(default!, default).ThrowsAsyncForAnyArgs(Error("El servidor no pudo consultar las órdenes."));

        var vm = await CargarAsync();

        Assert.True(vm.HayPlan);
        Assert.Equal("No pudimos consultar las órdenes", vm.AvisoOrdenes!.Titulo);
        Assert.False(vm.SinOrdenes);
        Assert.True(vm.HayAviso);

        Ordenes(0);
        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Null(vm.AvisoOrdenes);
        Assert.True(vm.SinOrdenes);
        Assert.False(vm.HayAviso);
    }

    [Fact]
    public async Task SinConexion_UsaMsg011()
    {
        _mantenimiento.ListarPlanesAsync(default!, default).ThrowsAsyncForAnyArgs(Error(Mensajes.Msg011, sinConexion: true));

        var vm = await CargarAsync();

        Assert.Equal(("Sin conexión", "MSG-011"), (vm.AvisoPlan!.Titulo, vm.AvisoPlan.Codigo));
        Assert.False(vm.SinPlan);
    }

    // ---------- Integración con la hoja de vida ----------

    private async Task<DetalleEquipoViewModel> HojaDeVidaAsync(Equipo equipo, string? rol, bool conServicios = true)
    {
        _equipos.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        var detalle = new DetalleEquipoViewModel(
            _equipos, new Reloj(), equipo.Id, equipo.PlacaInventario, () => { }, rol == "AC-02", null,
            conServicios ? _mantenimiento : null, conServicios ? _ordenes : null, rol);
        await detalle.CargarCommand.ExecuteAsync(null);
        return detalle;
    }

    [Theory]
    [InlineData("AC-01", true)]
    [InlineData("AC-02", true)]
    [InlineData("AC-03", true)]
    [InlineData("AC-05", true)]
    [InlineData("AC-07", true)]
    [InlineData("AC-04", false)]
    public async Task HojaDeVida_PestanaMantenimientos_SoloParaQuienConsultaLosPlanes(string rol, bool visible)
    {
        var detalle = await HojaDeVidaAsync(Equipo(), rol);

        Assert.Equal(visible, detalle.MuestraMantenimientos);
    }

    [Fact]
    public async Task HojaDeVida_AbrirMantenimientos_ConsultaUnaSolaVezConLaCalibracionDelEquipo()
    {
        var detalle = await HojaDeVidaAsync(Equipo(calibra: false), "AC-02");
        Assert.Null(detalle.Mantenimientos);

        detalle.EnMantenimientos = true;
        await Task.Yield();
        detalle.EnResumen = true;
        detalle.EnMantenimientos = true;

        Assert.False(detalle.EnResumen);
        Assert.Equal("No aplica", detalle.Mantenimientos!.Vigencia);
        await _mantenimiento.ReceivedWithAnyArgs(1).ListarPlanesAsync(default!, default);
        await _ordenes.ReceivedWithAnyArgs(1).ListarAsync(default!, default);
    }

    [Fact]
    public async Task HojaDeVida_SinServicios_NoMuestraLaPestana()
    {
        var detalle = await HojaDeVidaAsync(Equipo(), "AC-02", conServicios: false);

        detalle.EnMantenimientos = true;

        Assert.False(detalle.MuestraMantenimientos);
        Assert.Null(detalle.Mantenimientos);
    }

    [Fact]
    public async Task HojaDeVida_Documentos_EsUnaPestanaExcluyente()
    {
        var detalle = await HojaDeVidaAsync(Equipo(), "AC-02");

        detalle.EnDocumentos = true;

        Assert.True(detalle.EnDocumentos);
        Assert.False(detalle.EnResumen);
        Assert.False(detalle.EnMantenimientos);
        Assert.False(detalle.EnHistorial);
    }
}
