using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-010 Seguimiento de tiquetes (HU-017): lista por estado, consulta por código, detalle con historial y alcance por rol.</summary>
public class SeguimientoViewModelTests
{
    private static readonly DateTimeOffset Reporte = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private readonly IServicioTiquetes _servicio = Substitute.For<IServicioTiquetes>();
    private readonly List<FiltroTiquetes> _consultas = [];

    private static Tiquete Tiquete(
        string codigo, string estado = EstadosTiquete.Recibido, string criticidad = Criticidades.Alta, string descripcion = "La pantalla queda en negro.",
        DateTimeOffset? cierre = null, bool fueraDeServicio = false) =>
        new(Guid.NewGuid(), codigo, Guid.NewGuid(), "HSV-EQ-0412", "MX-1", "Monitor", "UCI", "No enciende", descripcion, criticidad, criticidad, estado,
            Reporte, cierre, Guid.NewGuid(), "Laura Gómez", fueraDeServicio ? "FUERA_DE_SERVICIO" : "OPERATIVO", fueraDeServicio);

    private static PaginaTiquetes Pagina(int total, params Tiquete[] tiquetes) => new(tiquetes, 1, SeguimientoViewModel.TamanoPagina, total);

    private static ErrorApi Error(string mensaje, bool sinConexion = false) => new(null, mensaje, new Dictionary<string, string>(), sinConexion);

    private async Task<SeguimientoViewModel> AbrirAsync(string rol, PaginaTiquetes? pagina = null)
    {
        _servicio.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _consultas.Add(llamada.ArgAt<FiltroTiquetes>(0));
            return pagina ?? Pagina(2, Tiquete("TQT-000002", EstadosTiquete.Asignado, Criticidades.Critica), Tiquete("TQT-000001"));
        });
        var vm = new SeguimientoViewModel(_servicio, EquiposPrueba.Sesion(rol));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private void ConHistorial(string codigo, Tiquete tiquete, params CambioTiquete[] cambios)
    {
        _servicio.ObtenerAsync(codigo, Arg.Any<CancellationToken>()).Returns(tiquete);
        _servicio.HistorialAsync(codigo, Arg.Any<CancellationToken>()).Returns(cambios);
    }

    private static CambioTiquete Cambio(string? anterior, string nuevo, int minutos, string? comentario = null, string usuario = "Laura Gómez") =>
        new(anterior, nuevo, comentario, Reporte.AddMinutes(minutos), Guid.NewGuid(), usuario);

    // ---------- Lista ----------

    [Fact]
    public async Task PersonalClinico_VeMisTiquetesConEstadoYCriticidadEnTexto()
    {
        var vm = await AbrirAsync("AC-04");

        Assert.Equal("Mis tiquetes", vm.Titulo);
        Assert.False(vm.PuedeVerSoloMios);
        Assert.Equal([new FiltroTiquetes(null, false, 1, SeguimientoViewModel.TamanoPagina)], _consultas);
        Assert.Equal(["TQT-000002", "TQT-000001"], vm.Filas.Select(f => f.Codigo));
        var primera = vm.Filas[0];
        Assert.Equal(("Asignado", TonoEstado.Advertencia), (primera.Estado, primera.Tono));
        Assert.Equal(("Crítica", TonoEstado.Critico), (primera.Criticidad, primera.TonoCriticidad));
        Assert.Equal(("HSV-EQ-0412 · Monitor", "UCI", "Laura Gómez"), (primera.Equipo, primera.Ubicacion, primera.Reportante));
        Assert.Equal("1–2 de 2 tiquetes", vm.ResumenPagina);
        Assert.False(vm.SinTiquetes);
    }

    [Fact]
    public async Task Ingeniero_VeTodosYPuedeFiltrarLosQueReporto()
    {
        var vm = await AbrirAsync("AC-02");
        Assert.Equal("Seguimiento de tiquetes", vm.Titulo);
        Assert.True(vm.PuedeVerSoloMios);

        vm.SoloMios = true;
        await Task.Yield();

        Assert.Equal(new FiltroTiquetes(null, true, 1, SeguimientoViewModel.TamanoPagina), _consultas[^1]);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-03")]
    [InlineData("AC-07")]
    public async Task QuienNoReporta_NoPideSoloLosSuyos(string rol)
    {
        var vm = await AbrirAsync(rol);

        vm.SoloMios = true;
        await Task.Yield();

        Assert.False(vm.PuedeVerSoloMios);
        Assert.All(_consultas, f => Assert.False(f.Mios));
    }

    [Fact]
    public async Task FiltrarPorEstado_ConsultaDeNuevoDesdeLaPrimeraPagina()
    {
        var vm = await AbrirAsync("AC-02", Pagina(45, Tiquete("TQT-000045")));
        await vm.PaginaSiguienteCommand.ExecuteAsync(null);
        Assert.Equal(2, _consultas[^1].Pagina);

        vm.FiltroEstado = SeguimientoViewModel.OpcionesEstado.Single(o => o.Valor == EstadosTiquete.EnEjecucion);
        await Task.Yield();

        Assert.Equal(new FiltroTiquetes(EstadosTiquete.EnEjecucion, false, 1, SeguimientoViewModel.TamanoPagina), _consultas[^1]);
        Assert.Equal("En ejecución", vm.FiltroEstado.Texto);
    }

    [Theory]
    [InlineData("AC-04", "Aún no has reportado fallas.")]
    [InlineData("AC-02", "Tu institución aún no tiene tiquetes.")]
    public async Task SinTiquetes_LoDiceSegunElRol(string rol, string texto)
    {
        var vm = await AbrirAsync(rol, Pagina(0));

        Assert.True(vm.SinTiquetes);
        Assert.Equal(texto, vm.TextoSinTiquetes);
        Assert.Equal("0 tiquetes", vm.ResumenPagina);
    }

    [Fact]
    public async Task ErrorAlListar_MuestraElAvisoConReintentar()
    {
        _servicio.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Throws(Error(Mensajes.Msg011, sinConexion: true));
        var vm = new SeguimientoViewModel(_servicio, EquiposPrueba.Sesion("AC-04"));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
        Assert.Empty(vm.Filas);
        Assert.False(vm.SinTiquetes);
    }

    // ---------- CA-HU-017-01: consulta por código y detalle ----------

    [Theory]
    [InlineData("TQT-000015", "TQT-000015")]
    [InlineData("  tqt-000015 ", "TQT-000015")]
    [InlineData("15", "TQT-000015")]
    [InlineData("000015", "TQT-000015")]
    [InlineData("TQT-15", null)]
    [InlineData("1234567", null)]
    [InlineData("OT-000015", null)]
    [InlineData("", null)]
    public void Normalizar_AceptaElCodigoOSoloElNumero(string texto, string? esperado) =>
        Assert.Equal(esperado, SeguimientoViewModel.Normalizar(texto));

    [Fact]
    public async Task ConsultarCodigo_AbreElDetalleConElHistorialDelMasRecienteAlMasAntiguo()
    {
        var vm = await AbrirAsync("AC-04");
        var cerrado = Tiquete("TQT-000015", EstadosTiquete.Cerrado, cierre: Reporte.AddHours(3));
        ConHistorial("TQT-000015", cerrado,
            Cambio(null, EstadosTiquete.Recibido, 0, "Falla reportada."),
            Cambio(EstadosTiquete.Recibido, EstadosTiquete.Asignado, 10, "Asignado al técnico de turno", "Carlos Ruiz"),
            Cambio(EstadosTiquete.Asignado, EstadosTiquete.Cerrado, 180, "Equipo reparado y probado", "Carlos Ruiz"));
        vm.Codigo = "15";

        await vm.ConsultarCodigoCommand.ExecuteAsync(null);

        Assert.Equal("TQT-000015", vm.Codigo);
        Assert.False(vm.EnLista);
        var detalle = vm.Detalle!;
        Assert.Equal(("TQT-000015", "Cerrado", TonoEstado.Exito), (detalle.Codigo, detalle.Estado, detalle.Tono));
        Assert.NotNull(detalle.Cierre);
        Assert.Equal(
            ["Asignado → Cerrado", "Recibido → Asignado", "Recibido"],
            detalle.Historial.Select(p => p.Titulo));
        Assert.Equal([true, false, false], detalle.Historial.Select(p => p.EsActual));
        Assert.Equal(("Equipo reparado y probado", "Carlos Ruiz"), (detalle.Historial[0].Comentario, detalle.Historial[0].Usuario));
    }

    [Theory]
    [InlineData("", SeguimientoViewModel.CodigoVacio)]
    [InlineData("TQT-15A", SeguimientoViewModel.CodigoInvalido)]
    public async Task ConsultarCodigo_VacioOMalEscrito_LoExplicaSinLlamarALaApi(string codigo, string error)
    {
        var vm = await AbrirAsync("AC-04");
        vm.Codigo = codigo;

        await vm.ConsultarCodigoCommand.ExecuteAsync(null);

        Assert.Equal(error, vm.ErrorCodigo);
        await _servicio.DidNotReceiveWithAnyArgs().ObtenerAsync(default!, default);
        Assert.True(vm.EnLista);
    }

    [Fact]
    public async Task ConsultarCodigo_QueNoPuedeVer_ExplicaJuntoAlCampoYSigueEnLaLista()
    {
        // CA-HU-017-02: el tiquete de otra persona responde 404 como si no existiera.
        var vm = await AbrirAsync("AC-04");
        _servicio.ObtenerAsync("TQT-000099", Arg.Any<CancellationToken>()).Throws(Error("No encontramos un tiquete con ese código entre los que puedes consultar. Revisa el código."));
        vm.Codigo = "TQT-000099";

        await vm.ConsultarCodigoCommand.ExecuteAsync(null);

        Assert.Equal("No encontramos un tiquete con ese código entre los que puedes consultar. Revisa el código.", vm.ErrorCodigo);
        Assert.Null(vm.Detalle);
        Assert.Null(vm.Aviso);

        vm.Codigo = "TQT-00009";
        Assert.Null(vm.ErrorCodigo);
    }

    [Fact]
    public async Task AbrirUnaFilaYVolver()
    {
        var vm = await AbrirAsync("AC-02");
        ConHistorial("TQT-000002", Tiquete("TQT-000002", EstadosTiquete.Asignado, Criticidades.Critica, descripcion: string.Empty, fueraDeServicio: true),
            Cambio(null, EstadosTiquete.Recibido, 0), Cambio(EstadosTiquete.Recibido, EstadosTiquete.Asignado, 5));

        await vm.AbrirCommand.ExecuteAsync(vm.Filas[0]);

        var detalle = vm.Detalle!;
        Assert.Equal(("Asignado", "Crítica", "Sin descripción."), (detalle.Estado, detalle.Criticidad, detalle.Descripcion));
        Assert.True(detalle.EquipoFueraDeServicio);
        Assert.Equal("El equipo HSV-EQ-0412 está «Fuera de servicio» hasta que se cierre la orden.", detalle.TextoFueraDeServicio);
        Assert.Null(detalle.Cierre);

        vm.VolverCommand.Execute(null);
        Assert.True(vm.EnLista);
    }

    [Fact]
    public async Task SinConexionAlAbrir_MuestraElAviso()
    {
        var vm = await AbrirAsync("AC-02");
        _servicio.ObtenerAsync("TQT-000001", Arg.Any<CancellationToken>()).Throws(Error(Mensajes.Msg011, sinConexion: true));
        _servicio.HistorialAsync("TQT-000001", Arg.Any<CancellationToken>()).Returns([]);

        await vm.AbrirCommand.ExecuteAsync(vm.Filas[1]);

        Assert.Equal(("Sin conexión", "MSG-011"), (vm.Aviso!.Titulo, vm.Aviso.Codigo));
        Assert.True(vm.EnLista);
    }

    [Fact]
    public async Task Recibir_AbreElTiqueteQueLlegaDeMsg005()
    {
        var vm = await AbrirAsync("AC-04");
        ConHistorial("TQT-000015", Tiquete("TQT-000015"), Cambio(null, EstadosTiquete.Recibido, 0, "Falla reportada."));

        vm.Recibir("TQT-000015");
        await Task.Yield();

        Assert.Equal("TQT-000015", vm.Detalle?.Codigo);
        Assert.Equal(["Recibido"], vm.Detalle!.Historial.Select(p => p.Titulo));
    }

    // ---------- Navegación ----------

    [Theory]
    [InlineData("AC-04")]
    [InlineData("AC-07")]
    public void Fabrica_UI010_AbreLaListaYLaCarga(string rol)
    {
        _servicio.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(Pagina(1, Tiquete("TQT-000001")));
        var servicios = new ServiceCollection()
            .AddSingleton(EquiposPrueba.Sesion(rol))
            .AddSingleton(_servicio)
            .AddTransient<SeguimientoViewModel>()
            .BuildServiceProvider();

        var pantalla = Assert.IsType<SeguimientoViewModel>(new FabricaPantallas(servicios).Crear("UI-010"));

        Assert.True(pantalla.Cargado);
        Assert.Equal(["TQT-000001"], pantalla.Filas.Select(f => f.Codigo));
    }

    [Fact]
    public void Marco_VerSeguimientoDesdeMsg005_AbreUi010ConElCodigo()
    {
        var reporte = new ReportarFallaPrueba();
        var seguimiento = new SeguimientoPrueba();
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear("UI-009").Returns(reporte);
        pantallas.Crear("UI-010").Returns(seguimiento);
        var shell = new ShellViewModel("AC-04", pantallas);
        Assert.Same(reporte, shell.Contenido);

        reporte.Pedir(new SolicitudNavegacion("UI-010", "TQT-000015"));

        Assert.Same(seguimiento, shell.Contenido);
        Assert.Equal(("UI-010", "Mis tiquetes"), (shell.OpcionActual!.Opcion.Id, shell.OpcionActual.Etiqueta));
        Assert.Equal(["TQT-000015"], seguimiento.Recibidos);

        // La pantalla anterior ya no puede mover el marco.
        reporte.Pedir(new SolicitudNavegacion("UI-009"));
        Assert.Same(seguimiento, shell.Contenido);
    }

    [Fact]
    public void Marco_OpcionQueElRolNoTiene_NoNavega()
    {
        var reporte = new ReportarFallaPrueba();
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear("UI-009").Returns(reporte);
        var shell = new ShellViewModel("AC-04", pantallas);

        reporte.Pedir(new SolicitudNavegacion("UI-011", "TQT-000015"));

        Assert.Same(reporte, shell.Contenido);
    }

    private sealed class ReportarFallaPrueba : ISolicitaNavegacion
    {
        public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

        public void Pedir(SolicitudNavegacion solicitud) => NavegacionSolicitada?.Invoke(this, solicitud);
    }

    private sealed class SeguimientoPrueba : IRecibeParametro
    {
        public List<string> Recibidos { get; } = [];

        public void Recibir(string parametro) => Recibidos.Add(parametro);
    }
}
