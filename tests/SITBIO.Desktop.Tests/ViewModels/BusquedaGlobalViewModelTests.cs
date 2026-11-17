using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// Búsqueda global de la barra superior (D-04): desde dos caracteres consulta la API, agrupa Equipos, Tiquetes y
/// Órdenes, se recorre con las flechas y Enter abre el detalle desde el marco.
/// </summary>
public class BusquedaGlobalViewModelTests
{
    private static readonly Guid EquipoId = Guid.NewGuid();
    private static readonly Guid OrdenId = Guid.NewGuid();
    private readonly IServicioBusqueda _servicio = Substitute.For<IServicioBusqueda>();

    private static ResultadoBusqueda Completo() => new(
        [new EquipoEncontrado(EquipoId, "MON-01", "SN-778", "Monitor de signos vitales", "UCI adultos")],
        [new TiqueteEncontrado("TQT-000015", "No enciende", "recibido", "MON-01")],
        [new OrdenEncontrada(OrdenId, "TQT-000015", "correctivo", "en_ejecucion", "MON-01")]);

    private BusquedaGlobalViewModel Crear(params string[] pantallas) =>
        new(_servicio, id => pantallas.Length == 0 || pantallas.Contains(id), TimeSpan.Zero);

    [Fact]
    public void ConDosCaracteres_ConsultaYAgrupa()
    {
        _servicio.BuscarAsync("mo", Arg.Any<CancellationToken>()).Returns(Completo());
        var vm = Crear();

        vm.Texto = " mo ";

        Assert.True(vm.Abierto);
        Assert.False(vm.SinResultados);
        Assert.Equal(["Equipos", "Tiquetes", "Órdenes de trabajo"], vm.Grupos.Select(g => g.Titulo));
        Assert.Equal(["MON-01", "TQT-000015", "TQT-000015"], vm.Resultados.Select(r => r.Codigo));
        Assert.All(vm.Resultados, r => Assert.True(r.EncabezaGrupo));
        var equipo = vm.Resultados[0];
        Assert.Equal(("Monitor de signos vitales", "UCI adultos · Serie SN-778"), (equipo.Titulo, equipo.Detalle));
        Assert.Equal(("Correctivo", "MON-01 · En ejecución"), (vm.Resultados[2].Titulo, vm.Resultados[2].Detalle));
        Assert.Same(vm.Resultados[0], vm.Seleccionado);
    }

    [Fact]
    public void ConUnCaracter_NoConsulta_NiAbreElPopup()
    {
        var vm = Crear();

        vm.Texto = "m";

        Assert.False(vm.Abierto);
        _servicio.DidNotReceiveWithAnyArgs().BuscarAsync(default!, default);
    }

    [Fact]
    public void SinResultados_MuestraElMensaje()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ResultadoBusqueda([], [], []));
        var vm = Crear();

        vm.Texto = "XYZ";

        Assert.True(vm.Abierto);
        Assert.True(vm.SinResultados);
        Assert.Equal("Sin resultados para «XYZ».", vm.TextoSinResultados);
    }

    [Fact]
    public void ErrorDeLaApi_MuestraElAviso()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "El servidor no pudo completar la búsqueda.", new Dictionary<string, string>()));
        var vm = Crear();

        vm.Texto = "MON";

        Assert.True(vm.Abierto);
        Assert.Equal("El servidor no pudo completar la búsqueda.", vm.Aviso);
        Assert.False(vm.SinResultados);
    }

    [Fact]
    public void GrupoSinPantallaEnElMenuDelRol_NoSeMuestra()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Completo());
        var vm = Crear("UI-005", "UI-012");

        vm.Texto = "TQT";

        Assert.Equal(["Equipos", "Órdenes de trabajo"], vm.Grupos.Select(g => g.Titulo));
    }

    [Fact]
    public void Flechas_RecorrenLosResultados_YEnterAbreElSeleccionado()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Completo());
        var vm = Crear();
        var aperturas = new List<SolicitudNavegacion>();
        vm.AperturaSolicitada += (_, s) => aperturas.Add(s);
        vm.Texto = "MON";

        vm.SeleccionarSiguienteCommand.Execute(null);
        Assert.Same(vm.Resultados[1], vm.Seleccionado);
        vm.SeleccionarAnteriorCommand.Execute(null);
        vm.SeleccionarAnteriorCommand.Execute(null);
        Assert.Same(vm.Resultados[2], vm.Seleccionado);

        vm.AbrirCommand.Execute(null);

        Assert.Equal([new SolicitudNavegacion("UI-012", OrdenId.ToString())], aperturas);
        Assert.Equal(string.Empty, vm.Texto);
        Assert.False(vm.Abierto);
    }

    [Fact]
    public void Escape_LimpiaLaCaja()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Completo());
        var vm = Crear();
        vm.Texto = "MON";

        vm.CerrarCommand.Execute(null);

        Assert.Equal((string.Empty, false, 0), (vm.Texto, vm.Abierto, vm.Resultados.Count));
    }

    [Fact]
    public async Task Marco_AbrirUnTiquete_LlevaAlSeguimientoConElCodigo()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Completo());
        var seguimiento = new PantallaPrueba();
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear("UI-010").Returns(seguimiento);
        var shell = new ShellViewModel("AC-02", pantallas, busqueda: _servicio);
        var busqueda = shell.BusquedaGlobal!;
        busqueda.Texto = "TQT";
        await busqueda.BusquedaEnCurso;

        busqueda.AbrirCommand.Execute(busqueda.Resultados.Single(r => r.Grupo == BusquedaGlobalViewModel.GrupoTiquetes));

        Assert.Same(seguimiento, shell.Contenido);
        Assert.Equal(["TQT-000015"], seguimiento.Recibidos);
    }

    [Fact]
    public async Task Marco_AbrirUnEquipo_PasaElIdYLaPlaca()
    {
        _servicio.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Completo());
        var equipos = new PantallaPrueba();
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear("UI-005").Returns(equipos);
        var shell = new ShellViewModel("AC-07", pantallas, busqueda: _servicio);
        shell.BusquedaGlobal!.Texto = "MON";
        await shell.BusquedaGlobal.BusquedaEnCurso;

        shell.BusquedaGlobal.AbrirCommand.Execute(null);

        Assert.Equal([$"{EquipoId}|MON-01"], equipos.Recibidos);
    }

    [Fact]
    public void Directiva_NoTieneBusquedaGlobal()
    {
        Assert.Null(new ShellViewModel("AC-06", busqueda: _servicio).BusquedaGlobal);
        Assert.False(new ShellViewModel("AC-02").TieneBusquedaGlobal);
        Assert.True(new ShellViewModel("AC-04", busqueda: _servicio).TieneBusquedaGlobal);
    }

    [Fact]
    public async Task Equipos_RecibirAbreLaHojaDeVida()
    {
        var servicio = Substitute.For<IServicioEquipos>();
        servicio.ObtenerAsync(EquipoId, Arg.Any<CancellationToken>()).Returns(EquiposPrueba.Equipo("MON-01"));
        var pantalla = new EquiposViewModel(servicio, Substitute.For<IServicioCatalogos>(), EquiposPrueba.Sesion("AC-02"), new EquiposPrueba.Reloj());

        pantalla.Recibir(EquiposViewModel.ParametroDetalle(EquipoId, "MON-01"));
        await Task.Yield();

        Assert.Equal("MON-01", pantalla.Detalle?.Placa);
        await servicio.Received().ObtenerAsync(EquipoId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("AC-02", EstadosOrden.Cerrada, false, "revision")]
    [InlineData("AC-03", EstadosOrden.EnEjecucion, false, "intervencion")]
    [InlineData("AC-01", EstadosOrden.Asignada, false, "lista")]
    [InlineData("AC-01", EstadosOrden.Cerrada, true, "intervencion")]
    public void Ordenes_RecibirAbreLaRevisionOLaIntervencionSegunElRol(string rol, string estado, bool intervencion, string esperado)
    {
        var ordenes = Substitute.For<IServicioOrdenes>();
        var orden = OrdenesViewModelTests.Orden(estado, intervencion: intervencion);
        ordenes.ObtenerAsync(orden.Id, Arg.Any<CancellationToken>()).Returns(orden);
        ordenes.ObtenerIntervencionAsync(orden.Id, Arg.Any<CancellationToken>()).Returns((Intervencion?)null);
        var equipos = Substitute.For<IServicioEquipos>();
        var equipo = EquiposPrueba.Equipo();
        equipos.ObtenerAsync(orden.EquipoId, Arg.Any<CancellationToken>()).Returns(equipo);
        var configuracion = Substitute.For<IServicioConfiguracionCampos>();
        configuracion.ObtenerAsync(equipo.TipoEquipoId, Arg.Any<CancellationToken>())
            .Returns(new ConfiguracionCampos(equipo.TipoEquipoId, "Monitor", null, null, [], []));
        var pantalla = new OrdenesViewModel(ordenes, equipos, configuracion, EquiposPrueba.Sesion(rol));

        pantalla.Recibir(orden.Id.ToString());

        var abierta = pantalla.Revision is not null ? "revision" : pantalla.Intervencion is not null ? "intervencion" : "lista";
        Assert.Equal(esperado, abierta);
    }

    [Fact]
    public void Ordenes_RecibirUnaOrdenQueYaNoEsVisible_MuestraElAviso()
    {
        var ordenes = Substitute.For<IServicioOrdenes>();
        ordenes.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "La orden ya no está disponible para ti. Actualiza la lista.", new Dictionary<string, string>()));
        var pantalla = new OrdenesViewModel(ordenes, Substitute.For<IServicioEquipos>(), Substitute.For<IServicioConfiguracionCampos>(), EquiposPrueba.Sesion("AC-03"));

        pantalla.Recibir(Guid.NewGuid().ToString());

        Assert.Equal("La orden ya no está disponible para ti. Actualiza la lista.", pantalla.Aviso?.Mensaje);
        Assert.True(pantalla.EnLista);
    }

    private sealed class PantallaPrueba : IRecibeParametro
    {
        public List<string> Recibidos { get; } = [];

        public void Recibir(string parametro) => Recibidos.Add(parametro);
    }
}
