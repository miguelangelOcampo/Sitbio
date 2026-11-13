using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-016 Centro de alertas (HU-020): al abrir la aplicación se ven los mantenimientos y calibraciones por vencer
/// (CA-HU-020-01) o vencidos (CA-HU-020-02); marcar como revisada saca la alerta del conteo y abrirla lleva a
/// UI-015 cerrando el panel (NAV-011).
/// </summary>
public class CentroAlertasViewModelTests
{
    private static readonly DateOnly Hoy = EquiposPrueba.Hoy;

    private readonly IServicioMantenimiento _servicio = Substitute.For<IServicioMantenimiento>();
    private readonly Guid _plan = Guid.NewGuid();
    private readonly Guid _monitor = Guid.NewGuid();

    private Alerta Vencida() =>
        new(TiposAlerta.Preventivo, TiposAlerta.Vencida, Hoy.AddDays(-4), -4, Guid.NewGuid(), "PL-001", "Bomba de infusión", "UCI",
            "Preventivo trimestral", _plan, Guid.NewGuid(), "asignada");

    private Alerta Calibracion() =>
        new(TiposAlerta.Calibracion, TiposAlerta.Proxima, Hoy.AddDays(5), 5, _monitor, "MON-07", "Monitor", "Urgencias", "Calibración", null, null, null);

    private async Task<CentroAlertasViewModel> AbrirAsync(int generadas = 0)
    {
        _servicio.AlertasAsync(Arg.Any<CancellationToken>()).Returns(new ResumenAlertas(20, [Vencida(), Calibracion()], generadas));
        var vm = new CentroAlertasViewModel(_servicio);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task AlAbrirLaAplicacion_MuestraLasAlertasConSuEstadoEnTexto()
    {
        var vm = await AbrirAsync();

        Assert.Equal((2, "2 alertas pendientes", "Mantenimientos y calibraciones de los próximos 20 días y los vencidos."), (vm.Pendientes, vm.TextoPendientes, vm.TextoAnticipacion));
        var vencida = vm.Filas[0];
        Assert.Equal(("Mantenimiento preventivo", "PL-001 · Bomba de infusión", "Vencida hace 4 días", TonoEstado.Critico), (vencida.Tipo, vencida.Equipo, vencida.Estado, vencida.Tono));
        Assert.Equal(("Orden preventiva asignada", "02/11/2026"), (vencida.Orden, vencida.Fecha));
        var calibracion = vm.Filas[1];
        Assert.Equal(("Calibración", "Vence en 5 días", TonoEstado.Advertencia, (string?)null), (calibracion.Tipo, calibracion.Estado, calibracion.Tono, calibracion.Orden));
        Assert.Null(vm.OrdenesGeneradas);
    }

    [Fact]
    public async Task OrdenesGeneradas_SeAvisan()
    {
        var vm = await AbrirAsync(generadas: 2);

        Assert.StartsWith("Se generaron 2 órdenes preventivas", vm.OrdenesGeneradas, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarcarRevisada_SaleDelConteo_YSeRecuerdaAlConsultarDeNuevo()
    {
        var vm = await AbrirAsync();

        vm.MarcarRevisadaCommand.Execute(vm.Filas[0]);
        Assert.Equal((1, true, TonoEstado.Neutro), (vm.Pendientes, vm.Filas[0].Revisada, vm.Filas[0].Tono));

        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal((1, true), (vm.Pendientes, vm.Filas[0].Revisada));

        vm.MarcarTodasRevisadasCommand.Execute(null);
        Assert.Equal((0, false, "Sin alertas pendientes", true), (vm.Pendientes, vm.HayPendientes, vm.TextoPendientes, vm.HayRevisadas));
    }

    [Fact]
    public async Task AbrirAlerta_LlevaAlPlanOALaCalibracion_YCierraElPanel()
    {
        var vm = await AbrirAsync();
        var solicitudes = new List<SolicitudNavegacion>();
        vm.NavegacionSolicitada += (_, s) => solicitudes.Add(s);
        vm.AlternarCommand.Execute(null);
        Assert.True(vm.Abierto);

        vm.AbrirAlertaCommand.Execute(vm.Filas[0]);
        Assert.False(vm.Abierto);
        vm.AbrirAlertaCommand.Execute(vm.Filas[1]);

        Assert.Equal(
            [new SolicitudNavegacion("UI-015", $"plan:{_plan}"), new SolicitudNavegacion("UI-015", $"calibracion:{_monitor}")],
            solicitudes);
    }

    [Fact]
    public async Task Alternar_AbreYConsultaDeNuevo_OCierra()
    {
        var vm = await AbrirAsync();

        vm.AlternarCommand.Execute(null);
        Assert.True(vm.Abierto);
        await _servicio.Received(2).AlertasAsync(Arg.Any<CancellationToken>());

        vm.AlternarCommand.Execute(null);
        Assert.False(vm.Abierto);
        await _servicio.Received(2).AlertasAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinAlertas_YConError()
    {
        _servicio.AlertasAsync(Arg.Any<CancellationToken>()).Returns(new ResumenAlertas(15, [], 0));
        var vm = new CentroAlertasViewModel(_servicio);
        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal((true, 0, false), (vm.SinAlertas, vm.Pendientes, vm.HayPendientes));

        _servicio.AlertasAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorApi(null, "El servidor no pudo consultar las alertas.", new Dictionary<string, string>()));
        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal(("No se pudieron consultar las alertas", true, false), (vm.Aviso!.Titulo, vm.Aviso.PuedeReintentar, vm.SinAlertas));
    }
}
