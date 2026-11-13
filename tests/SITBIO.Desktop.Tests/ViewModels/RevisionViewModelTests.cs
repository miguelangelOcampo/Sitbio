using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-014 Revisión y firma (HU-021): el Ingeniero ve la intervención en solo lectura, firma, confirma MSG-018
/// y vuelve con la orden aprobada; la calibración vencida (RN-007, MSG-013), la orden ya aprobada (RN-004) y
/// la firma rechazada se muestran sin salir de la pantalla.
/// </summary>
public class RevisionViewModelTests
{
    private const string Msg013 = "La calibración de este equipo venció el 01/10/2026. Registre una nueva calibración antes de cerrar la orden como «Operativo».";

    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IServicioConfiguracionCampos _configuracion = Substitute.For<IServicioConfiguracionCampos>();
    private readonly List<Orden?> _vueltas = [];
    private readonly Orden _orden = OrdenesViewModelTests.Orden(EstadosOrden.Cerrada, intervencion: true) with
    {
        FechaCierre = new DateTimeOffset(2026, 11, 13, 15, 30, 0, TimeSpan.Zero),
    };

    private async Task<RevisionViewModel> AbrirAsync()
    {
        var equipo = EquiposPrueba.Equipo();
        _equipos.ObtenerAsync(_orden.EquipoId, Arg.Any<CancellationToken>()).Returns(equipo);
        _configuracion.ObtenerAsync(equipo.TipoEquipoId, Arg.Any<CancellationToken>())
            .Returns(new ConfiguracionCampos(equipo.TipoEquipoId, "Bomba", 1, DateTimeOffset.UtcNow, [], []));
        _ordenes.ObtenerIntervencionAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new Intervencion(
            Guid.NewGuid(), _orden.Id, "Sensor descalibrado", "Calibración", [new Repuesto("Sensor", 2, 15000m)], 40000m, 70000m, 90, "Operativo",
            new Dictionary<string, System.Text.Json.JsonElement>(), 1, DateTimeOffset.UtcNow, false));
        var vm = new RevisionViewModel(_ordenes, _equipos, _configuracion, _orden, _vueltas.Add);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Abrir_MuestraLaIntervencionEnSoloLectura_SinSuBarra()
    {
        var vm = await AbrirAsync();

        Assert.Equal("UI-014", RevisionViewModel.IdPantalla);
        Assert.False(vm.Detalle.Editable);
        Assert.False(vm.Detalle.MuestraBarra);
        Assert.False(vm.Detalle.CerrarOrdenCommand.CanExecute(null));
        Assert.Equal(("Sensor descalibrado", "Operativo"), (vm.Detalle.Diagnostico, vm.Detalle.EstadoSalida));
        Assert.Equal(("TQT-000007", "Carlos Ruiz"), (vm.Referencia, vm.Tecnico));
        Assert.Contains("2026", vm.Cerrada);
        Assert.True(vm.AprobarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Aprobar_PideConfirmarMsg018_YAlConfirmarVuelveConLaOrdenAprobada()
    {
        var vm = await AbrirAsync();
        _ordenes.AprobarAsync(_orden.Id, "Laura Gómez", Arg.Any<CancellationToken>())
            .Returns(_orden with { Estado = EstadosOrden.Aprobada, FirmaAprobacion = "Laura Gómez" });
        vm.Firma = "  Laura Gómez ";

        vm.AprobarCommand.Execute(null);

        Assert.True(vm.Confirmando);
        Assert.Equal("Al aprobar y firmar, la orden TQT-000007 y su intervención quedarán inalterables. ¿Desea continuar?", vm.TextoConfirmacion);
        await _ordenes.DidNotReceiveWithAnyArgs().AprobarAsync(default, default!, default);

        await vm.ConfirmarAprobacionCommand.ExecuteAsync(null);

        Assert.False(vm.Confirmando);
        var vuelta = Assert.Single(_vueltas)!;
        Assert.Equal((EstadosOrden.Aprobada, "Laura Gómez"), (vuelta.Estado, vuelta.FirmaAprobacion));
    }

    [Fact]
    public async Task Cancelar_LaConfirmacion_NoFirma()
    {
        var vm = await AbrirAsync();
        vm.Firma = "Laura Gómez";

        vm.AprobarCommand.Execute(null);
        vm.CancelarAprobacionCommand.Execute(null);

        Assert.False(vm.Confirmando);
        await _ordenes.DidNotReceiveWithAnyArgs().AprobarAsync(default, default!, default);
        vm.CancelarCommand.Execute(null);
        Assert.Equal([(Orden?)null], _vueltas);
    }

    [Theory]
    [InlineData("   ", RevisionViewModel.FirmaObligatoria)]
    [InlineData(null, "La firma admite hasta 100 caracteres.")]
    public async Task FirmaVaciaOMuyLarga_SeMarcaSinPedirConfirmacion(string? firma, string error)
    {
        var vm = await AbrirAsync();
        vm.Firma = firma ?? new string('a', CodigosOrden.LongitudMaximaFirma + 1);

        vm.AprobarCommand.Execute(null);

        Assert.False(vm.Confirmando);
        Assert.Equal(error, vm.ErrorFirma);
        vm.Firma = "Laura Gómez";
        Assert.Null(vm.ErrorFirma);
    }

    [Fact]
    public async Task CalibracionVencida_MuestraMsg013_YSigueEnLaPantalla()
    {
        var vm = await AbrirAsync();
        _ordenes.AprobarAsync(_orden.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(CodigosOrden.CalibracionVencida, Msg013, new Dictionary<string, string>()));
        vm.Firma = "Laura Gómez";

        vm.AprobarCommand.Execute(null);
        await vm.ConfirmarAprobacionCommand.ExecuteAsync(null);

        Assert.Equal(("Calibración vencida", Msg013, "RN-007"), (vm.Aviso!.Titulo, vm.Aviso.Mensaje, vm.Aviso.Codigo));
        Assert.True(vm.AprobarCommand.CanExecute(null));
        Assert.Empty(_vueltas);
    }

    [Fact]
    public async Task YaAprobada_Rn004_YaNoPermiteAprobar()
    {
        var vm = await AbrirAsync();
        _ordenes.AprobarAsync(_orden.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(CodigosOrden.Firmada, "La orden ya está aprobada y firmada; no se puede modificar (RN-004).", new Dictionary<string, string>()));
        vm.Firma = "Laura Gómez";

        vm.AprobarCommand.Execute(null);
        await vm.ConfirmarAprobacionCommand.ExecuteAsync(null);

        Assert.Equal(("La orden ya fue aprobada", "RN-004"), (vm.Aviso!.Titulo, vm.Aviso.Codigo));
        Assert.False(vm.PuedeAprobar);
        Assert.False(vm.AprobarCommand.CanExecute(null));
    }

    [Fact]
    public async Task FirmaRechazadaPorLaApi_SeMuestraJuntoAlCampo()
    {
        var vm = await AbrirAsync();
        _ordenes.AprobarAsync(_orden.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Datos inválidos.", new Dictionary<string, string> { ["firma"] = "La firma es obligatoria." }));
        vm.Firma = "Laura Gómez";

        vm.AprobarCommand.Execute(null);
        await vm.ConfirmarAprobacionCommand.ExecuteAsync(null);

        Assert.Equal("La firma es obligatoria.", vm.ErrorFirma);
        Assert.Null(vm.Aviso);
    }

    [Fact]
    public async Task SinConexion_AvisaConMsg011()
    {
        var vm = await AbrirAsync();
        _ordenes.AprobarAsync(_orden.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        vm.Firma = "Laura Gómez";

        vm.AprobarCommand.Execute(null);
        await vm.ConfirmarAprobacionCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011"), (vm.Aviso!.Titulo, vm.Aviso.Codigo));
        Assert.True(vm.AprobarCommand.CanExecute(null));
    }
}
