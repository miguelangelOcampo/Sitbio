using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>RF-031: fotografía del equipo en la hoja de vida, la lista (UI-005) y Preferencias (prototipo FotoEquipo.tsx).</summary>
public class FotoEquipoViewModelTests
{
    private const string Ruta = @"C:\Fotos\monitor.jpg";
    private static readonly byte[] Original = [0xFF, 0xD8, 1, 2, 3];
    private static readonly FotoReducida Reducida = new(new byte[20 * 1024], new byte[26 * 1024], 512, 384);
    private static readonly byte[] MiniaturaApi = [1, 2, 8];
    private static readonly byte[] VistaApi = [5, 1, 2];

    private readonly IServicioFotoEquipo _fotos = Substitute.For<IServicioFotoEquipo>();
    private readonly IReductorFoto _reductor = Substitute.For<IReductorFoto>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly Guid _id = Guid.NewGuid();

    private FotoEquipoViewModel Dialogo(byte[]? vistaActual = null, string ruta = Ruta, long bytes = 3_494_000)
    {
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns(ruta);
        _dialogo.Tamano(ruta).Returns(bytes);
        _dialogo.Leer(ruta).Returns(Original);
        _reductor.Reducir(Original).Returns(Reducida);
        return new FotoEquipoViewModel(_fotos, _reductor, _dialogo, _id, "HSV-EQ-0412", vistaActual);
    }

    private async Task<DetalleEquipoViewModel> HojaDeVidaAsync(Equipo equipo, bool puedeEditar = true, bool verFotos = true)
    {
        _equipos.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        _fotos.DescargarAsync(equipo.Id, 128, Arg.Any<CancellationToken>()).Returns(MiniaturaApi);
        _fotos.DescargarAsync(equipo.Id, 512, Arg.Any<CancellationToken>()).Returns(VistaApi);
        var detalle = new DetalleEquipoViewModel(
            _equipos, new Reloj(), equipo.Id, equipo.PlacaInventario, () => { }, puedeEditar,
            dialogo: _dialogo, fotos: _fotos, reductor: _reductor, verFotos: verFotos);
        await detalle.CargarCommand.ExecuteAsync(null);
        return detalle;
    }

    // ---------- Diálogo ----------

    [Fact]
    public void ElegirImagen_OtroFormatoOMasDe10Mb_NoSeReduce()
    {
        var pdf = Dialogo(ruta: @"C:\Fotos\manual.pdf");
        pdf.ElegirImagenCommand.Execute(null);
        var grande = Dialogo(bytes: 11L * 1024 * 1024);
        grande.ElegirImagenCommand.Execute(null);

        Assert.Equal("Usa una imagen JPG, PNG o WebP.", pdf.Error);
        Assert.Equal("La imagen supera 10 MB.", grande.Error);
        _reductor.DidNotReceiveWithAnyArgs().Reducir(default!);
    }

    [Fact]
    public void ImagenQueNoSePuedeLeer_MuestraElError()
    {
        var vm = Dialogo();
        _reductor.Reducir(Original).Throws(new ImagenNoValida());

        vm.ElegirImagenCommand.Execute(null);

        Assert.Equal("No pudimos leer la imagen.", vm.Error);
        Assert.Null(vm.Nueva);
    }

    [Fact]
    public async Task Guardar_ReduceAntesDeSubir_YExigeElAvisoDeDatosPersonales()
    {
        var vm = Dialogo();
        CambioFoto? cambio = null;
        vm.Cambiada += (_, c) => cambio = c;

        vm.ElegirImagenCommand.Execute(null);
        var sinAviso = vm.GuardarCommand.CanExecute(null);
        vm.AvisoAceptado = true;
        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Agregar fotografía a HSV-EQ-0412", vm.Titulo);
        Assert.False(sinAviso);
        Assert.Equal("Original: 3.412 KB → guardada: 46 KB", vm.TextoPeso);
        Assert.Same(Reducida.Vista, vm.VistaPrevia);
        await _fotos.Received(1).GuardarAsync(_id, Reducida, Arg.Any<CancellationToken>());
        Assert.Same(Reducida.Miniatura, cambio?.Miniatura);
        Assert.Equal("HSV-EQ-0412: se guardó reducida (46 KB en lugar de 3.412 KB).", cambio?.Confirmacion.Mensaje);
    }

    [Fact]
    public async Task ElPlanSinEspacio_DejaElDialogoAbiertoConElMensaje()
    {
        var vm = Dialogo();
        _fotos.GuardarAsync(_id, Reducida, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("MSG-019", "Tu institución alcanzó el máximo de almacenamiento del plan.", new Dictionary<string, string>()));
        var cambiada = false;
        vm.Cambiada += (_, _) => cambiada = true;
        vm.ElegirImagenCommand.Execute(null);
        vm.AvisoAceptado = true;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(cambiada);
        Assert.Equal("MSG-019", vm.Aviso?.Codigo);
    }

    [Fact]
    public async Task Quitar_SoloConFotoYSinHaberElegidoOtra()
    {
        var sinFoto = Dialogo();
        var conFoto = Dialogo(vistaActual: VistaApi);
        CambioFoto? cambio = null;
        conFoto.Cambiada += (_, c) => cambio = c;

        var puedeQuitar = conFoto.PuedeQuitar;
        await conFoto.QuitarCommand.ExecuteAsync(null);

        Assert.False(sinFoto.PuedeQuitar);
        Assert.True(puedeQuitar);
        Assert.Equal("Cambiar fotografía de HSV-EQ-0412", conFoto.Titulo);
        await _fotos.Received(1).QuitarAsync(_id, Arg.Any<CancellationToken>());
        Assert.Null(cambio?.Miniatura);
        Assert.Equal("Fotografía quitada", cambio?.Confirmacion.Titulo);
    }

    // ---------- Hoja de vida (UI-006) ----------

    [Fact]
    public async Task HojaDeVida_ConFoto_MuestraLaMiniaturaYLaAmplia()
    {
        var detalle = await HojaDeVidaAsync(Equipo() with { TieneFoto = true });

        await detalle.VerFotoCommand.ExecuteAsync(null);

        Assert.True(detalle.MuestraFoto);
        Assert.Same(MiniaturaApi, detalle.Miniatura);
        Assert.Same(VistaApi, detalle.FotoAmpliada);
        Assert.Equal("Cambiar la fotografía", detalle.TextoBotonFoto);
    }

    [Fact]
    public async Task HojaDeVida_PreferenciaIcono_NoDescargaLaMiniaturaPeroSePuedeVer()
    {
        var detalle = await HojaDeVidaAsync(Equipo() with { TieneFoto = true }, verFotos: false);

        Assert.False(detalle.MuestraFoto);
        Assert.True(detalle.PuedeVerFoto);
        await _fotos.DidNotReceive().DescargarAsync(Arg.Any<Guid>(), 128, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HojaDeVida_SinFoto_IconoYSoloAc02PuedeAgregarla()
    {
        var ingeniero = await HojaDeVidaAsync(Equipo());
        var consulta = await HojaDeVidaAsync(Equipo(), puedeEditar: false);
        var deBaja = await HojaDeVidaAsync(Equipo(estado: "BAJA", baja: DateTimeOffset.Now));

        Assert.False(ingeniero.MuestraFoto);
        Assert.False(ingeniero.PuedeVerFoto);
        Assert.True(ingeniero.PuedeCambiarFoto);
        Assert.Equal("Agregar una fotografía", ingeniero.TextoBotonFoto);
        Assert.False(consulta.PuedeCambiarFoto);
        Assert.False(deBaja.PuedeCambiarFoto);
        await _fotos.DidNotReceiveWithAnyArgs().DescargarAsync(default, default, default);
    }

    [Fact]
    public async Task HojaDeVida_AlGuardarLaFoto_ReemplazaElIconoYConfirma()
    {
        var detalle = await HojaDeVidaAsync(Equipo());
        Dialogo();
        await detalle.AbrirDialogoFotoCommand.ExecuteAsync(null);
        var dialogo = detalle.DialogoFoto!;
        dialogo.ElegirImagenCommand.Execute(null);
        dialogo.AvisoAceptado = true;

        await dialogo.GuardarCommand.ExecuteAsync(null);

        Assert.Null(detalle.DialogoFoto);
        Assert.True(detalle.TieneFoto);
        Assert.Same(Reducida.Miniatura, detalle.Miniatura);
        Assert.Equal("Fotografía guardada", detalle.Confirmacion?.Titulo);
    }

    // ---------- Lista (UI-005) y Preferencias ----------

    [Fact]
    public async Task Lista_PideLaMiniaturaSoloDeLosEquiposConFoto()
    {
        var conFoto = Equipo("HSV-EQ-0001") with { TieneFoto = true };
        var sinFoto = Equipo("HSV-EQ-0002");
        _equipos.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(Pagina(2, conFoto, sinFoto));
        _fotos.DescargarAsync(conFoto.Id, 128, Arg.Any<CancellationToken>()).Returns(MiniaturaApi);
        var preferencias = Substitute.For<IServicioPreferencias>();
        preferencias.Leer().Returns(new Preferencias());
        var lista = new EquiposViewModel(_equipos, Substitute.For<IServicioCatalogos>(), Sesion("AC-03"), new Reloj(), fotos: _fotos, preferencias: preferencias);

        await lista.CargarCommand.ExecuteAsync(null);

        Assert.Same(MiniaturaApi, lista.Filas[0].Foto.Imagen);
        Assert.False(lista.Filas[1].Foto.HayImagen);
        await _fotos.DidNotReceive().DescargarAsync(sinFoto.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lista_PreferenciaIcono_NoPideMiniaturas()
    {
        _equipos.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(Pagina(1, Equipo() with { TieneFoto = true }));
        var preferencias = Substitute.For<IServicioPreferencias>();
        preferencias.Leer().Returns(new Preferencias(VerFotosEquipo: false));
        var lista = new EquiposViewModel(_equipos, Substitute.For<IServicioCatalogos>(), Sesion("AC-03"), new Reloj(), fotos: _fotos, preferencias: preferencias);

        await lista.CargarCommand.ExecuteAsync(null);

        Assert.False(lista.Filas[0].Foto.HayImagen);
        await _fotos.DidNotReceiveWithAnyArgs().DescargarAsync(default, default, default);
    }

    [Fact]
    public void Preferencias_IconoDelTipo_SeGuardaEnLaEstacion()
    {
        var preferencias = Substitute.For<IServicioPreferencias>();
        preferencias.Leer().Returns(new Preferencias(Temas.Oscuro));
        var perfil = new PerfilViewModel(new SesionActual(), preferencias, Substitute.For<ITemaAplicacion>());

        var antes = perfil.VerFotosEquipo;
        perfil.VerIconosEquipo = true;

        Assert.True(antes);
        preferencias.Received(1).Guardar(new Preferencias(Temas.Oscuro, VerFotosEquipo: false));
    }
}
