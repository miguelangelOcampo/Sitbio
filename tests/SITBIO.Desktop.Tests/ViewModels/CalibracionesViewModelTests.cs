using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-017 Calibraciones (HU-022): historial con vencimiento y vigencia en texto; el Técnico registra la calibración
/// con fecha, patrón, resultado y vencimiento (CA-HU-022-01) solo de equipos que requieren calibración
/// (CA-HU-022-03) y la corrige sin aprobar; el Ingeniero aprueba con confirmación. Hoy = 06/11/2026 en Bogotá.
/// </summary>
public class CalibracionesViewModelTests
{
    private static readonly DateOnly Hoy = EquiposPrueba.Hoy;

    private readonly IServicioMantenimiento _servicio = Substitute.For<IServicioMantenimiento>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private IReadOnlyList<Calibracion> _calibraciones = [];

    public CalibracionesViewModelTests()
    {
        _servicio.ListarCalibracionesAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>())
            .Returns(_ => new PaginaCalibraciones(_calibraciones, 1, CalibracionesViewModel.TamanoPagina, _calibraciones.Count));
    }

    private static Calibracion Calibracion(
        DateOnly? vence = null,
        bool aprobada = false,
        string placa = "MON-07",
        string resultado = ResultadosCalibracion.Conforme,
        string? certificado = "CERT-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), placa, "Monitor", "Urgencias", null, null, Hoy.AddDays(-10), "Analizador Fluke IDA-5", resultado,
            certificado, vence ?? Hoy.AddMonths(12), (vence ?? Hoy.AddMonths(12)) >= Hoy, Guid.NewGuid(), "Ana Mora",
            aprobada ? Guid.NewGuid() : null, aprobada ? "Luis Díaz" : null, aprobada);

    private async Task<CalibracionesViewModel> AbrirAsync(string rol, params Calibracion[] calibraciones)
    {
        _calibraciones = calibraciones;
        var vm = new CalibracionesViewModel(_servicio, _equipos, EquiposPrueba.Sesion(rol), new EquiposPrueba.Reloj());
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Historial_MuestraElVencimientoYLaVigenciaEnTexto()
    {
        var vm = await AbrirAsync("AC-05", Calibracion(Hoy.AddDays(-3)), Calibracion(Hoy.AddDays(20), aprobada: true), Calibracion(Hoy.AddDays(200), certificado: null));

        Assert.Equal((3, "1–3 de 3 calibraciones", true), (vm.Total, vm.ResumenPagina, vm.SoloConsulta));
        var vencida = vm.Filas[0];
        Assert.Equal(("MON-07 · Monitor", "27/10/2026", "Vencida hace 3 días", TonoEstado.Critico, "Pendiente de aprobación"), (vencida.Equipo, vencida.Fecha, vencida.Vigencia, vencida.Tono, vencida.Aprobacion));
        Assert.Equal(("Vigente · 20 días", TonoEstado.Advertencia, "Aprobada por Luis Díaz"), (vm.Filas[1].Vigencia, vm.Filas[1].Tono, vm.Filas[1].Aprobacion));
        Assert.Equal((TonoEstado.Exito, "Sin certificado", "Conforme"), (vm.Filas[2].Tono, vm.Filas[2].Certificado, vm.Filas[2].Resultado));
        Assert.All(vm.Filas, f => Assert.False(f.PuedeCorregir || f.PuedeAprobar));
    }

    [Fact]
    public async Task Tecnico_BuscaSoloEquiposQueRequierenCalibracion_YRegistra_ConElVencimientoCalculado()
    {
        var monitor = EquiposPrueba.Equipo("MON-07");
        var estetoscopio = EquiposPrueba.Equipo("MON-EST", calibra: false);
        _equipos.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(EquiposPrueba.Pagina(2, monitor, estetoscopio));
        _servicio.RegistrarCalibracionAsync(Arg.Any<DatosCalibracion>(), Arg.Any<CancellationToken>()).Returns(Calibracion(Hoy.AddDays(-2).AddMonths(12)));
        var vm = await AbrirAsync("AC-03");

        vm.NuevaCalibracionCommand.Execute(null);
        Assert.Equal((true, "06/11/2026", "Registrar calibración"), (vm.FormularioAbierto, vm.FechaCalibracion, vm.TituloFormulario));
        vm.Busqueda = "MON";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);
        await _equipos.Received().ListarAsync(Arg.Is<FiltroEquipos>(f => f.Calibracion == true), Arg.Any<CancellationToken>());
        Assert.Equal([monitor], vm.Coincidencias);

        vm.ElegirEquipoCommand.Execute(monitor);
        vm.FechaCalibracion = "04/11/2026";
        vm.Patron = "  Analizador Fluke IDA-5 ";
        vm.Certificado = "CERT-2026-0451";
        Assert.Equal("Vence el 04/11/2027.", vm.VencimientoCalculado);
        await vm.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).RegistrarCalibracionAsync(
            new DatosCalibracion(monitor.Id, new DateOnly(2026, 11, 4), "Analizador Fluke IDA-5", ResultadosCalibracion.Conforme, "CERT-2026-0451", null),
            Arg.Any<CancellationToken>());
        Assert.False(vm.FormularioAbierto);
        Assert.StartsWith("Calibración de MON-07 registrada. Vence el", vm.Exito, StringComparison.Ordinal);
        await _servicio.Received(2).ListarCalibracionesAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_SinDatos_MuestraLosErroresJuntoALosCampos_SinLlamarALaApi()
    {
        var vm = await AbrirAsync("AC-03");
        vm.NuevaCalibracionCommand.Execute(null);
        vm.FechaCalibracion = "07/11/2026";
        vm.Vencimiento = "31/02/2027";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(
            (CalibracionesViewModel.SinEquipo, CalibracionesViewModel.FechaFutura, CalibracionesViewModel.SinPatron, CalibracionesViewModel.FechaInvalida),
            (vm.ErrorEquipo, vm.ErrorFecha, vm.ErrorPatron, vm.ErrorVencimiento));
        await _servicio.DidNotReceive().RegistrarCalibracionAsync(Arg.Any<DatosCalibracion>(), Arg.Any<CancellationToken>());

        vm.FechaCalibracion = "04/11/2026";
        vm.Vencimiento = "01/11/2026";
        await vm.GuardarCommand.ExecuteAsync(null);
        Assert.Equal(CalibracionesViewModel.VencimientoAnterior, vm.ErrorVencimiento);
    }

    [Fact]
    public async Task Registrar_LaApiRechazaElEquipo_MuestraSuMensaje()
    {
        var monitor = EquiposPrueba.Equipo("MON-07");
        _servicio.RegistrarCalibracionAsync(Arg.Any<DatosCalibracion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("CAL-001", "El equipo MON-07 no requiere calibración (RN-010).", new Dictionary<string, string>()));
        var vm = await AbrirAsync("AC-03");
        vm.NuevaCalibracionCommand.Execute(null);
        vm.ElegirEquipoCommand.Execute(monitor);
        vm.Patron = "Analizador";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(("CAL-001", true), (vm.AvisoFormulario!.Codigo, vm.FormularioAbierto));
    }

    [Fact]
    public async Task Tecnico_CorrigeLaSinAprobar_YNoLaAprobada()
    {
        var pendiente = Calibracion();
        var aprobada = Calibracion(aprobada: true);
        var corregida = pendiente with { NumeroCertificado = "CERT-9" };
        _servicio.CorregirCalibracionAsync(pendiente.Id, Arg.Any<DatosCorreccionCalibracion>(), Arg.Any<CancellationToken>()).Returns(corregida);
        var vm = await AbrirAsync("AC-03", pendiente, aprobada);

        Assert.Equal((true, false, false), (vm.Filas[0].PuedeCorregir, vm.Filas[0].PuedeAprobar, vm.Filas[1].PuedeCorregir));
        vm.AbrirCorreccionCommand.Execute(vm.Filas[1]);
        Assert.False(vm.FormularioAbierto);

        vm.AbrirCorreccionCommand.Execute(vm.Filas[0]);
        Assert.Equal(("Corregir la calibración de MON-07", "27/10/2026", "Analizador Fluke IDA-5", "CERT-1"), (vm.TituloFormulario, vm.FechaCalibracion, vm.Patron, vm.Certificado));
        vm.Certificado = "CERT-9";
        await vm.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CorregirCalibracionAsync(
            pendiente.Id,
            Arg.Is<DatosCorreccionCalibracion>(d => d.NumeroCertificado == "CERT-9" && d.FechaVencimiento == pendiente.FechaVencimiento),
            Arg.Any<CancellationToken>());
        Assert.Equal("Certificado CERT-9", vm.Filas[0].Certificado);
    }

    [Fact]
    public async Task Ingeniero_ApruebaConConfirmacion_YNoRegistra()
    {
        var pendiente = Calibracion();
        _servicio.AprobarCalibracionAsync(pendiente.Id, Arg.Any<CancellationToken>()).Returns(pendiente with { Aprobada = true, AprobadoPor = "Luis Díaz" });
        var vm = await AbrirAsync("AC-02", pendiente);

        vm.NuevaCalibracionCommand.Execute(null);
        Assert.Equal((false, false, true), (vm.FormularioAbierto, vm.PuedeRegistrar, vm.Filas[0].PuedeAprobar));

        await vm.AprobarCommand.ExecuteAsync(vm.Filas[0]);
        Assert.Same(vm.Filas[0], vm.ConfirmandoAprobar);
        await _servicio.DidNotReceive().AprobarCalibracionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await vm.AprobarCommand.ExecuteAsync(vm.Filas[0]);
        await _servicio.Received(1).AprobarCalibracionAsync(pendiente.Id, Arg.Any<CancellationToken>());
        Assert.Equal(("Aprobada por Luis Díaz", false, (FilaCalibracion?)null), (vm.Filas[0].Aprobacion, vm.Filas[0].PuedeAprobar, vm.ConfirmandoAprobar));
        Assert.StartsWith("Calibración de MON-07 del 27/10/2026 aprobada", vm.Exito, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesdeUnaAlerta_FiltraPorElEquipo_YSinConexionAvisa()
    {
        var calibracion = Calibracion(Hoy.AddDays(-1));
        _calibraciones = [calibracion];
        var vm = new CalibracionesViewModel(_servicio, _equipos, EquiposPrueba.Sesion("AC-03"), new EquiposPrueba.Reloj());

        await vm.MostrarEquipoAsync(calibracion.EquipoId);
        Assert.Equal(("Solo el equipo MON-07", "Vencida ayer"), (vm.TextoFiltroEquipo, vm.Filas[0].Vigencia));
        await _servicio.Received(1).ListarCalibracionesAsync(Arg.Is<FiltroCalibraciones>(f => f.EquipoId == calibracion.EquipoId), Arg.Any<CancellationToken>());

        _servicio.ListarCalibracionesAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Sin conexión con el servidor.", new Dictionary<string, string>(), sinConexion: true));
        await vm.QuitarFiltroEquipoCommand.ExecuteAsync(null);
        Assert.Equal(("Sin conexión", "MSG-011", true, false), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar, vm.HayFiltroEquipo));
    }
}
