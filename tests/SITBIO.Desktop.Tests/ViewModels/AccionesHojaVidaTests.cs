using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-006: acciones del encabezado de la hoja de vida (prototipo Equipos.tsx). «Reportar falla» abre UI-009
/// con el equipo ya elegido, «Hoja de vida PDF» muestra la vista previa y guarda el PDF de la API, y el menú
/// «…» reúne la fotografía y la baja. Cada acción se ve según la matriz RBAC.
/// </summary>
public class AccionesHojaVidaTests
{
    private readonly IServicioEquipos _servicio = Substitute.For<IServicioEquipos>();
    private readonly IServicioReportes _reportes = Substitute.For<IServicioReportes>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();
    private readonly IServicioFotoEquipo _fotos = Substitute.For<IServicioFotoEquipo>();
    private readonly List<(Guid Id, string Placa)> _reportadas = [];

    private async Task<DetalleEquipoViewModel> AbrirAsync(string rol, Equipo? equipo = null, bool puedeEditar = false, bool verFotos = true)
    {
        equipo ??= Equipo();
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        var detalle = new DetalleEquipoViewModel(
            _servicio, new Reloj(), equipo.Id, equipo.PlacaInventario, () => { }, puedeEditar, rol: rol, dialogo: _dialogo,
            reportes: _reportes, reportarFalla: (id, placa) => _reportadas.Add((id, placa)), fotos: _fotos, verFotos: verFotos);
        await detalle.CargarCommand.ExecuteAsync(null);
        return detalle;
    }

    [Theory]
    [InlineData("AC-02", true)]
    [InlineData("AC-04", true)]
    [InlineData("AC-01", false)]
    [InlineData("AC-03", false)]
    [InlineData("AC-05", false)]
    [InlineData("AC-07", false)]
    public async Task ReportarFalla_SoloQuienReporta(string rol, bool visible)
    {
        var detalle = await AbrirAsync(rol);

        Assert.Equal(visible, detalle.PuedeReportarFalla);
    }

    [Fact]
    public async Task ReportarFalla_AbreUi009ConElEquipo()
    {
        var equipo = Equipo();
        var detalle = await AbrirAsync("AC-04", equipo);

        detalle.ReportarFallaCommand.Execute(null);

        Assert.Equal([(equipo.Id, equipo.PlacaInventario)], _reportadas);
    }

    [Fact]
    public async Task ReportarFalla_NoEnUnEquipoDeBaja()
    {
        var detalle = await AbrirAsync("AC-02", Equipo(estado: EstadosEquipo.Baja));

        detalle.ReportarFallaCommand.Execute(null);

        Assert.False(detalle.PuedeReportarFalla);
        Assert.Empty(_reportadas);
    }

    [Theory]
    [InlineData("AC-01", true)]
    [InlineData("AC-02", true)]
    [InlineData("AC-05", true)]
    [InlineData("AC-07", true)]
    [InlineData("AC-03", false)]
    [InlineData("AC-04", false)]
    [InlineData("AC-06", false)]
    public async Task HojaVidaPdf_LosRolesDeLaPoliticaDeLaApi(string rol, bool visible)
    {
        var detalle = await AbrirAsync(rol);

        Assert.Equal(visible, detalle.PuedeHojaVidaPdf);
    }

    [Fact]
    public async Task HojaVidaPdf_TambienConElEquipoDeBaja()
    {
        var detalle = await AbrirAsync("AC-07", Equipo(estado: EstadosEquipo.Baja));

        Assert.True(detalle.PuedeHojaVidaPdf);
    }

    [Fact]
    public async Task HojaVidaPdf_MuestraLaVistaPreviaYGuardaElPdfDeLaApi()
    {
        var equipo = Equipo();
        _reportes.HojaVidaAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado([7, 8], "HV_HSV-EQ-0412.pdf"));
        _dialogo.ElegirDestino("HV_HSV-EQ-0412.pdf").Returns(@"C:\Descargas\HV_HSV-EQ-0412.pdf");
        var detalle = await AbrirAsync("AC-02", equipo);

        detalle.AbrirHojaVidaPdfCommand.Execute(null);
        Assert.True(detalle.VistaPreviaPdf);
        await detalle.GuardarHojaVidaPdfCommand.ExecuteAsync(null);

        _dialogo.Received(1).Guardar(@"C:\Descargas\HV_HSV-EQ-0412.pdf", Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 7, 8 })));
        Assert.False(detalle.VistaPreviaPdf);
        Assert.Equal("Hoja de vida guardada", detalle.Confirmacion!.Titulo);
    }

    [Fact]
    public async Task HojaVidaPdf_SiCancelaElDialogo_NoGuardaYSigueAbierto()
    {
        var equipo = Equipo();
        _reportes.HojaVidaAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado([1], "HV.pdf"));
        _dialogo.ElegirDestino(Arg.Any<string>()).Returns((string?)null);
        var detalle = await AbrirAsync("AC-02", equipo);

        detalle.AbrirHojaVidaPdfCommand.Execute(null);
        await detalle.GuardarHojaVidaPdfCommand.ExecuteAsync(null);

        _dialogo.DidNotReceive().Guardar(Arg.Any<string>(), Arg.Any<byte[]>());
        Assert.True(detalle.VistaPreviaPdf);
    }

    [Fact]
    public async Task HojaVidaPdf_ElErrorDeLaApiQuedaEnElDialogo()
    {
        var equipo = Equipo();
        _reportes.HojaVidaAsync(equipo.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorApi("AUTH-003", "No tienes permiso.", new Dictionary<string, string>(), false));
        var detalle = await AbrirAsync("AC-02", equipo);

        detalle.AbrirHojaVidaPdfCommand.Execute(null);
        await detalle.GuardarHojaVidaPdfCommand.ExecuteAsync(null);

        Assert.True(detalle.VistaPreviaPdf);
        Assert.Equal(("No pudimos generar la hoja de vida", "AUTH-003"), (detalle.AvisoPdf!.Titulo, detalle.AvisoPdf.Codigo));
        Assert.False(detalle.GenerandoPdf);

        detalle.CerrarHojaVidaPdfCommand.Execute(null);
        Assert.False(detalle.VistaPreviaPdf);
    }

    [Fact]
    public async Task HojaVidaPdf_QuienNoTienePermiso_NoAbreLaVistaPrevia()
    {
        var detalle = await AbrirAsync("AC-04");

        detalle.AbrirHojaVidaPdfCommand.Execute(null);

        Assert.False(detalle.VistaPreviaPdf);
    }

    [Fact]
    public async Task MenuMasAcciones_ConDarDeBajaParaElIngeniero_YOcultoSinAcciones()
    {
        var ingeniero = await AbrirAsync("AC-02", puedeEditar: true);
        var clinico = await AbrirAsync("AC-04");

        Assert.True(ingeniero.PuedeDarDeBaja);
        Assert.True(ingeniero.HayMasAcciones);
        Assert.False(clinico.HayMasAcciones);
    }

    [Fact]
    public async Task Pantalla_PideAbrirUi009ConElIdDelEquipo()
    {
        var equipo = Equipo();
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        var pantalla = new EquiposViewModel(_servicio, Substitute.For<IServicioCatalogos>(), Sesion("AC-04"), new Reloj(), reportes: _reportes);
        var pedidos = new List<SolicitudNavegacion>();
        pantalla.NavegacionSolicitada += (_, s) => pedidos.Add(s);

        pantalla.Recibir(EquiposViewModel.ParametroDetalle(equipo.Id, equipo.PlacaInventario));
        await Task.Yield();
        pantalla.Detalle!.ReportarFallaCommand.Execute(null);

        Assert.Equal([new SolicitudNavegacion("UI-009", equipo.Id.ToString())], pedidos);
    }

    // ---------- I-10: foto en la vista previa de la hoja de vida ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HojaVidaPdf_LaVistaPreviaMuestraLaFotoDe512_AunqueSePrefieraElIcono(bool verFotos)
    {
        var equipo = Equipo() with { TieneFoto = true };
        _fotos.DescargarAsync(equipo.Id, 512, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);
        var detalle = await AbrirAsync("AC-02", equipo, verFotos: verFotos);

        await detalle.AbrirHojaVidaPdfCommand.ExecuteAsync(null);

        Assert.Equal([1, 2, 3], detalle.FotoPdf);
        Assert.False(detalle.SinFotoPdf);
    }

    [Fact]
    public async Task HojaVidaPdf_SinFoto_MuestraElIconoDelTipo()
    {
        var detalle = await AbrirAsync("AC-02");

        await detalle.AbrirHojaVidaPdfCommand.ExecuteAsync(null);

        Assert.True(detalle.VistaPreviaPdf);
        Assert.True(detalle.SinFotoPdf);
        Assert.Equal("Icono.Tipo.Monitor", detalle.IconoTipo);
        await _fotos.DidNotReceiveWithAnyArgs().DescargarAsync(default, default, default);
    }

    [Fact]
    public async Task HojaVidaPdf_SiLaFotoNoLlega_LaVistaPreviaSigueConElIcono()
    {
        var equipo = Equipo() with { TieneFoto = true };
        _fotos.DescargarAsync(equipo.Id, 512, Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorApi(null, "Sin red", new Dictionary<string, string>(), true));
        var detalle = await AbrirAsync("AC-02", equipo, verFotos: false);

        await detalle.AbrirHojaVidaPdfCommand.ExecuteAsync(null);

        Assert.True(detalle.VistaPreviaPdf);
        Assert.True(detalle.SinFotoPdf);
        Assert.Null(detalle.AvisoPdf);
    }
}
