using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-021 (HU-026) y UI-022 (HU-007, HU-027).</summary>
public class ReportesViewModelTests
{
    private readonly IServicioReportes _servicio = Substitute.For<IServicioReportes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static Indicadores Kpi(bool costos = true) => new(
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
        new CumplimientoPreventivo(8, 6, 75), 6, 2880, new CostoPorEquipo(costos ? 500000m : null, 20, costos ? 25000m : null), new FueraDeServicio(2, 20, 10), costos);

    private ExportacionViewModel Exportacion(string rol)
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("token", DateTimeOffset.UtcNow.AddHours(1), rol, false));
        return new ExportacionViewModel(_servicio, _equipos, _dialogo, sesion);
    }

    // ---------- UI-021 ----------

    [Fact]
    public async Task Tablero_MuestraLosCincoIndicadores()
    {
        _servicio.IndicadoresAsync(null, null, Arg.Any<CancellationToken>()).Returns(Kpi());
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("75 %", "6 de 8 preventivas cerradas", "6 h", "2880 h"), (vm.Cumplimiento, vm.DetalleCumplimiento, vm.Mttr, vm.Mtbf));
        Assert.Equal(("2", "10 % de 20 equipos activos"), (vm.Fuera, vm.DetalleFuera));
        Assert.Contains("25.000", vm.Costo);
        Assert.Equal("Del 01/11/2026 al 30/11/2026", vm.Periodo);
        Assert.Null(vm.AvisoCostos);
    }

    [Fact]
    public async Task Tablero_SinCostos_LoAvisa_YFechaInvalidaNoConsulta()
    {
        _servicio.IndicadoresAsync(default, default, default).ReturnsForAnyArgs(Kpi(costos: false));
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal(("—", "Costo no disponible"), (vm.Costo, vm.DetalleCosto));
        Assert.NotNull(vm.AvisoCostos);

        vm.Desde = "31-13-2026";
        await vm.CargarCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorFechas);
        await _servicio.ReceivedWithAnyArgs(1).IndicadoresAsync(default, default, default);
    }

    [Fact]
    public async Task Tablero_ErrorDeLaApi_MuestraAviso()
    {
        _servicio.IndicadoresAsync(default, default, default).ThrowsAsyncForAnyArgs(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("Sin conexión", vm.Aviso?.Titulo);
        Assert.False(vm.HayDatos);
    }

    // ---------- UI-022 ----------

    [Fact]
    public async Task Exportar_DescargaElFormatoElegido_YLoGuardaDondeDiga()
    {
        var vm = Exportacion("AC-05");
        vm.Desde = "01/11/2026";
        vm.Hasta = "30/11/2026";
        vm.Formato = ExportacionViewModel.Formatos[1];
        _servicio.HistorialOrdenesAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "xlsx", Arg.Any<CancellationToken>())
            .Returns(new ArchivoDescargado([1, 2], "historial.xlsx"));
        _dialogo.ElegirDestino("historial.xlsx").Returns(@"C:\Reportes\historial.xlsx");

        await vm.ExportarHistorialCommand.ExecuteAsync(null);

        _dialogo.Received(1).Guardar(@"C:\Reportes\historial.xlsx", Arg.Is<byte[]>(b => b.Length == 2));
        Assert.Equal(("Archivo guardado", @"C:\Reportes\historial.xlsx"), (vm.Confirmacion?.Titulo, vm.Confirmacion?.Mensaje));
        Assert.True(vm.PuedeExportarHistorial);
    }

    [Fact]
    public async Task Exportar_CanceladoOFechasInvalidas_NoGuarda()
    {
        var vm = Exportacion("AC-02");
        vm.Desde = "30/11/2026";
        vm.Hasta = "01/11/2026";
        await vm.ExportarHistorialCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorFechas);

        vm.Desde = "01/11/2026";
        vm.Hasta = "30/11/2026";
        _servicio.HistorialOrdenesAsync(default, default, default!, default).ReturnsForAnyArgs(new ArchivoDescargado([1], "h.pdf"));
        _dialogo.ElegirDestino("h.pdf").Returns((string?)null);
        await vm.ExportarHistorialCommand.ExecuteAsync(null);

        _dialogo.DidNotReceiveWithAnyArgs().Guardar(default!, default!);
        Assert.Null(vm.Confirmacion);
    }

    [Fact]
    public async Task HojaDeVida_ElAuditorBuscaElEquipo_YGeneraElPdf()
    {
        var vm = Exportacion("AC-07");
        var equipo = Equipo();
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>()).Returns(new PaginaEquipos([equipo], 1, 10, 1));
        _servicio.HojaVidaAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado([9], "hoja-de-vida.pdf"));
        _dialogo.ElegirDestino("hoja-de-vida.pdf").Returns(@"C:\hv.pdf");

        await vm.GenerarHojaVidaCommand.ExecuteAsync(null);
        Assert.Equal("Elige un equipo", vm.Aviso?.Titulo);

        vm.Placa = "HSV";
        await vm.BuscarEquipoCommand.ExecuteAsync(null);
        await vm.GenerarHojaVidaCommand.ExecuteAsync(null);

        Assert.False(vm.PuedeExportarHistorial);
        Assert.Equal(equipo, vm.EquipoElegido);
        _dialogo.Received(1).Guardar(@"C:\hv.pdf", Arg.Any<byte[]>());
    }
}
