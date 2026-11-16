using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-009 Reportar falla (HU-003, HU-018): búsqueda del equipo, validación, MSG-006 para fallas críticas y MSG-005 con el código.</summary>
public class ReportarFallaViewModelTests
{
    private readonly IServicioTiquetes _tiquetes = Substitute.For<IServicioTiquetes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IPortapapeles _portapapeles = Substitute.For<IPortapapeles>();
    private readonly Equipo _monitor = EquiposPrueba.Equipo("HSV-EQ-0412");
    private readonly Equipo _baja = EquiposPrueba.Equipo("HSV-EQ-0499", estado: "BAJA");
    private DatosTiquete? _enviado;

    private static ErrorApi Error(string? codigo, string mensaje, IReadOnlyDictionary<string, string>? errores = null, bool sinConexion = false) =>
        new(codigo, mensaje, errores ?? new Dictionary<string, string>(), sinConexion);

    private async Task<ReportarFallaViewModel> AbrirAsync()
    {
        _tiquetes.TiposFallaAsync(Arg.Any<CancellationToken>()).Returns(["Alarma", "No enciende"]);
        _equipos.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(EquiposPrueba.Pagina(0));
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "0412" || f.Placa == "HSV"), Arg.Any<CancellationToken>())
            .Returns(EquiposPrueba.Pagina(2, _monitor, _baja));
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Serie == "HSV"), Arg.Any<CancellationToken>())
            .Returns(EquiposPrueba.Pagina(1, _monitor));
        _tiquetes.ReportarAsync(Arg.Any<DatosTiquete>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _enviado = llamada.ArgAt<DatosTiquete>(0);
            return Creado(_enviado.Criticidad == Criticidades.Critica);
        });
        var vm = new ReportarFallaViewModel(_tiquetes, _equipos, _portapapeles);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private Tiquete Creado(bool fueraDeServicio) =>
        new(Guid.NewGuid(), "TQT-000015", _monitor.Id, _monitor.PlacaInventario, _monitor.NumeroSerie, _monitor.TipoEquipo, _monitor.Ubicacion,
            "No enciende", string.Empty, fueraDeServicio ? Criticidades.Critica : Criticidades.Alta, Criticidades.Alta, "recibido",
            DateTimeOffset.UtcNow, null, Guid.NewGuid(), "Laura Gómez", fueraDeServicio ? "FUERA_DE_SERVICIO" : "OPERATIVO", fueraDeServicio);

    private static OpcionCriticidad Nivel(string valor) => ReportarFallaViewModel.OpcionesCriticidad.Single(o => o.Valor == valor);

    private async Task<ReportarFallaViewModel> DiligenciadoAsync(string criticidad)
    {
        var vm = await AbrirAsync();
        vm.Busqueda = "0412";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);
        vm.ElegirEquipoCommand.Execute(vm.Coincidencias[0]);
        vm.TipoFalla = "No enciende";
        vm.Criticidad = Nivel(criticidad);
        return vm;
    }

    // ---------- Carga ----------

    [Fact]
    public async Task Cargar_TraeLosTiposDeFallaDelCatalogo()
    {
        var vm = await AbrirAsync();

        Assert.True(vm.Cargado);
        Assert.Equal(["Alarma", "No enciende"], vm.TiposFalla);
        Assert.False(vm.SinTiposFalla);
        Assert.Equal(["Crítica", "Alta", "Media", "Baja"], ReportarFallaViewModel.OpcionesCriticidad.Select(o => o.Texto));
        Assert.Equal("0/500", vm.ContadorDescripcion);
    }

    [Fact]
    public async Task Cargar_SinTiposDeFalla_LoAvisa()
    {
        _tiquetes.TiposFallaAsync(Arg.Any<CancellationToken>()).Returns([]);
        var vm = new ReportarFallaViewModel(_tiquetes, _equipos, _portapapeles);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinTiposFalla);
    }

    [Fact]
    public async Task Cargar_SinConexion_AvisaYPermiteReintentar()
    {
        _tiquetes.TiposFallaAsync(Arg.Any<CancellationToken>()).ThrowsAsync(Error(null, "Sin conexión.", sinConexion: true));
        var vm = new ReportarFallaViewModel(_tiquetes, _equipos, _portapapeles);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("MSG-011", true), (vm.Aviso!.Codigo, vm.Aviso.PuedeReintentar));
    }

    // ---------- Búsqueda del equipo (CMP-UI-003) ----------

    [Fact]
    public async Task Buscar_PorPlacaYSerie_SinRepetirNiOfrecerEquiposDeBaja()
    {
        var vm = await AbrirAsync();

        vm.Busqueda = "HSV";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);

        Assert.Equal([_monitor], vm.Coincidencias);
        await _equipos.Received().ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV" && f.Tamano == 8), Arg.Any<CancellationToken>());
        await _equipos.Received().ListarAsync(Arg.Is<FiltroEquipos>(f => f.Serie == "HSV"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Buscar_ConMenosDeDosCaracteres_NoLlamaALaApi()
    {
        var vm = await AbrirAsync();

        vm.Busqueda = "H";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);

        Assert.Empty(vm.Coincidencias);
        await _equipos.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
    }

    [Fact]
    public async Task Buscar_SinResultados_LoDice()
    {
        var vm = await AbrirAsync();

        vm.Busqueda = "ZZZ";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);

        Assert.True(vm.SinCoincidencias);
    }

    [Fact]
    public async Task ElegirEquipo_LoMuestraYEscribirDeNuevoLoDescarta()
    {
        var vm = await AbrirAsync();
        vm.Busqueda = "0412";
        await vm.BuscarEquiposCommand.ExecuteAsync(null);

        vm.ElegirEquipoCommand.Execute(_monitor);

        Assert.Same(_monitor, vm.EquipoSeleccionado);
        Assert.Equal("HSV-EQ-0412", vm.Busqueda);
        Assert.Empty(vm.Coincidencias);
        Assert.Equal("HSV-EQ-0412 · Monitor de signos vitales · UCI adultos · Operativo", vm.TextoEquipo);

        vm.Busqueda = "HSV-EQ";
        Assert.Null(vm.EquipoSeleccionado);
    }

    // ---------- CA-HU-003-02: validación en español, sin enviar ----------

    [Fact]
    public async Task Enviar_SinEquipo_MuestraLaValidacionYNoLlamaALaApi()
    {
        var vm = await AbrirAsync();
        vm.TipoFalla = "No enciende";
        vm.Criticidad = Nivel(Criticidades.Alta);

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal("Selecciona el equipo que presenta la falla.", vm.ErrorEquipo);
        await _tiquetes.DidNotReceiveWithAnyArgs().ReportarAsync(default!, default);
    }

    [Fact]
    public async Task Enviar_SinNada_MarcaLosTresObligatorios()
    {
        var vm = await AbrirAsync();

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorEquipo);
        Assert.Equal("Elige el tipo de falla.", vm.ErrorTipoFalla);
        Assert.Equal("Elige la criticidad: crítica, alta, media o baja.", vm.ErrorCriticidad);
        Assert.Null(vm.ErrorDescripcion);
    }

    [Fact]
    public async Task Descripcion_LlevaContadorYNoPasaDe500()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);

        vm.Descripcion = new string('x', 501);
        Assert.Equal("501/500", vm.ContadorDescripcion);
        Assert.NotNull(vm.ErrorDescripcion);
        await vm.EnviarCommand.ExecuteAsync(null);

        await _tiquetes.DidNotReceiveWithAnyArgs().ReportarAsync(default!, default);
    }

    // ---------- CA-HU-003-01 y CA-HU-018-01: envío y MSG-005 ----------

    [Fact]
    public async Task Enviar_NoCritica_EnviaLosCuatroDatosYMuestraElCodigo()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);
        vm.Descripcion = "  La pantalla queda en negro.  ";

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal(new DatosTiquete(_monitor.Id, "No enciende", "La pantalla queda en negro.", Criticidades.Alta), _enviado);
        Assert.True(vm.Enviado);
        Assert.False(vm.Formulario);
        Assert.Equal("Reporte enviado. Su tiquete es TQT-000015. Úselo para consultar el estado de la atención.", vm.TextoResultado);
    }

    [Fact]
    public async Task Enviar_SinDescripcion_LaMandaVacia()
    {
        var vm = await DiligenciadoAsync(Criticidades.Baja);

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Null(_enviado!.Descripcion);
    }

    // ---------- CA-HU-018-02: MSG-006 antes de enviar una falla crítica ----------

    [Fact]
    public async Task Enviar_Critica_PrimeroPideConfirmacion()
    {
        var vm = await DiligenciadoAsync(Criticidades.Critica);

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.True(vm.ConfirmandoCritica);
        Assert.Equal("Al reportar la falla como crítica, el equipo HSV-EQ-0412 quedará «Fuera de servicio» hasta que se cierre la orden.", vm.TextoConfirmacionCritica);
        await _tiquetes.DidNotReceiveWithAnyArgs().ReportarAsync(default!, default);

        await vm.ConfirmarCriticaCommand.ExecuteAsync(null);

        Assert.Equal(Criticidades.Critica, _enviado!.Criticidad);
        Assert.True(vm.Creado!.EquipoFueraDeServicio);
        Assert.False(vm.ConfirmandoCritica);
    }

    [Fact]
    public async Task CambiarCriticidad_CierraMsg006SinEnviar()
    {
        var vm = await DiligenciadoAsync(Criticidades.Critica);
        await vm.EnviarCommand.ExecuteAsync(null);

        vm.CambiarCriticidadCommand.Execute(null);

        Assert.False(vm.ConfirmandoCritica);
        Assert.Null(vm.Criticidad);
        await _tiquetes.DidNotReceiveWithAnyArgs().ReportarAsync(default!, default);
    }

    [Fact]
    public async Task ElegirOtraCriticidad_RetiraLaConfirmacion()
    {
        var vm = await DiligenciadoAsync(Criticidades.Critica);
        await vm.EnviarCommand.ExecuteAsync(null);

        vm.Criticidad = Nivel(Criticidades.Alta);

        Assert.False(vm.ConfirmandoCritica);
    }

    // ---------- Errores de la API ----------

    [Fact]
    public async Task ErrorDeValidacionDeLaApi_LoMuestraEnElCampo()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);
        _tiquetes.ReportarAsync(Arg.Any<DatosTiquete>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error(null, "Uno o más datos no son válidos.", new Dictionary<string, string> { ["tipoFalla"] = "Ese tipo de falla no está activo en el catálogo de tu institución." }));

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal("Ese tipo de falla no está activo en el catálogo de tu institución.", vm.ErrorTipoFalla);
        Assert.Equal("No se envió el reporte", vm.Aviso!.Titulo);
        Assert.False(vm.Enviado);
    }

    [Fact]
    public async Task SinConexionAlEnviar_ConservaElFormulario()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);
        _tiquetes.ReportarAsync(Arg.Any<DatosTiquete>(), Arg.Any<CancellationToken>()).ThrowsAsync(Error(null, "x", sinConexion: true));

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-011", vm.Aviso!.Codigo);
        Assert.Same(_monitor, vm.EquipoSeleccionado);
        Assert.True(vm.Formulario);
    }

    // ---------- Después de enviar ----------

    [Fact]
    public async Task CopiarCodigoYReportarOtra()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);
        await vm.EnviarCommand.ExecuteAsync(null);
        _portapapeles.Copiar("TQT-000015").Returns(true);

        vm.CopiarCodigoCommand.Execute(null);
        Assert.Equal("Código TQT-000015 copiado.", vm.TextoCopiado);

        vm.ReportarOtraCommand.Execute(null);
        Assert.True(vm.Formulario);
        Assert.Null(vm.EquipoSeleccionado);
        Assert.Equal((string.Empty, (string?)null, (OpcionCriticidad?)null), (vm.Busqueda, vm.TipoFalla, vm.Criticidad));
    }

    [Fact]
    public async Task VerSeguimiento_PideAbrirUi010ConElCodigo()
    {
        var vm = await DiligenciadoAsync(Criticidades.Alta);
        await vm.EnviarCommand.ExecuteAsync(null);
        SolicitudNavegacion? pedida = null;
        vm.NavegacionSolicitada += (_, solicitud) => pedida = solicitud;

        vm.VerSeguimientoCommand.Execute(null);

        Assert.Equal(new SolicitudNavegacion("UI-010", "TQT-000015"), pedida);
    }

    // ---------- Navegación ----------

    [Theory]
    [InlineData("AC-04")]
    [InlineData("AC-02")]
    public void Fabrica_UI009_AbreElFormularioYCargaElCatalogo(string rol)
    {
        _tiquetes.TiposFallaAsync(Arg.Any<CancellationToken>()).Returns(["No enciende"]);
        var servicios = new ServiceCollection()
            .AddSingleton(EquiposPrueba.Sesion(rol))
            .AddSingleton(_tiquetes)
            .AddSingleton(_equipos)
            .AddSingleton(_portapapeles)
            .AddTransient<ReportarFallaViewModel>()
            .BuildServiceProvider();

        var pantalla = Assert.IsType<ReportarFallaViewModel>(new FabricaPantallas(servicios).Crear("UI-009"));

        Assert.True(pantalla.Cargado);
        Assert.Equal(["No enciende"], pantalla.TiposFalla);
    }
}
