using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using Sitbio.Desktop.Views;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// J-12: «Cambiar foto» y «Quitar foto» en Mi perfil. La foto se reduce antes de subirla y se ve al instante en el
/// avatar (la misma <see cref="FotoPerfilActual"/>); sin foto, las iniciales.
/// </summary>
public class FotoPerfilTests
{
    private const string Ruta = @"C:\Fotos\yo.png";
    private static readonly byte[] Original = [1, 2, 3];
    private static readonly FotoReducida Reducida = new([7, 7], [8, 8, 8], 512, 512);

    private readonly IServicioFotoPerfil _servicio = Substitute.For<IServicioFotoPerfil>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();
    private readonly IReductorFoto _reductor = Substitute.For<IReductorFoto>();
    private readonly IServicioPreferencias _preferencias = Substitute.For<IServicioPreferencias>();
    private readonly FotoPerfilActual _foto = new();

    public FotoPerfilTests()
    {
        _preferencias.Leer().Returns(new Preferencias());
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns(Ruta);
        _dialogo.Tamano(Ruta).Returns(3);
        _dialogo.Leer(Ruta).Returns(Original);
        _reductor.Reducir(Original).Returns(Reducida);
    }

    private PerfilViewModel Perfil() =>
        new(EquiposPrueba.Sesion("AC-04"), _preferencias, Substitute.For<ITemaAplicacion>(), _foto, _servicio, _dialogo, _reductor);

    [Fact]
    public async Task CambiarFoto_LaReduceLaSubeYElAvatarLaMuestra()
    {
        var vm = Perfil();
        Assert.False(vm.QuitarFotoCommand.CanExecute(null));

        await vm.CambiarFotoCommand.ExecuteAsync(null);

        await _servicio.Received(1).GuardarAsync(Reducida, Arg.Any<CancellationToken>());
        Assert.Equal(Reducida.Miniatura, _foto.Miniatura);
        Assert.Equal(("Foto actualizada", false), (vm.AvisoFoto!.Titulo, vm.AvisoFoto.EsError));
        Assert.True(vm.QuitarFotoCommand.CanExecute(null));
    }

    [Fact]
    public async Task QuitarFoto_VuelvenLasIniciales()
    {
        _foto.Miniatura = [1];
        var vm = Perfil();

        await vm.QuitarFotoCommand.ExecuteAsync(null);

        await _servicio.Received(1).QuitarAsync(Arg.Any<CancellationToken>());
        Assert.True(_foto.SinFoto);
        Assert.Equal("Foto quitada", vm.AvisoFoto?.Titulo);
    }

    [Theory]
    [InlineData(@"C:\Fotos\yo.gif", 3, "Usa una imagen JPG, PNG o WebP.")]
    [InlineData(Ruta, 11L * 1024 * 1024, "La imagen supera 10 MB.")]
    public async Task ArchivoQueNoSirve_LoDiceSinSubirNada(string ruta, long tamano, string mensaje)
    {
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns(ruta);
        _dialogo.Tamano(ruta).Returns(tamano);
        var vm = Perfil();

        await vm.CambiarFotoCommand.ExecuteAsync(null);

        Assert.Equal((mensaje, true), (vm.AvisoFoto!.Mensaje, vm.AvisoFoto.EsError));
        await _servicio.DidNotReceiveWithAnyArgs().GuardarAsync(default!, default);
    }

    [Fact]
    public async Task ImagenQueWindowsNoLee_LoDice()
    {
        _reductor.Reducir(Original).Throws(new ImagenNoValida());
        var vm = Perfil();

        await vm.CambiarFotoCommand.ExecuteAsync(null);

        Assert.Equal("No pudimos leer la imagen.", vm.AvisoFoto?.Mensaje);
    }

    [Fact]
    public async Task Cancelar_NoHaceNada()
    {
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        var vm = Perfil();

        await vm.CambiarFotoCommand.ExecuteAsync(null);

        Assert.Null(vm.AvisoFoto);
        _dialogo.DidNotReceiveWithAnyArgs().Leer(default!);
    }

    [Fact]
    public async Task SiLaApiFalla_LaFotoAnteriorSeQueda()
    {
        _foto.Miniatura = [5];
        _servicio.GuardarAsync(default!, default).ThrowsAsyncForAnyArgs(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), true));
        var vm = Perfil();

        await vm.CambiarFotoCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 5 }, _foto.Miniatura);
        Assert.Equal(("Sin conexión", "MSG-011"), (vm.AvisoFoto!.Titulo, vm.AvisoFoto.Codigo));
        Assert.False(vm.GuardandoFoto);
    }

    [Fact]
    public void SinServicios_NoSeOfreceCambiarLaFoto()
    {
        var vm = new PerfilViewModel(EquiposPrueba.Sesion("AC-04"), _preferencias, Substitute.For<ITemaAplicacion>());

        Assert.False(vm.PuedeCambiarFoto);
        Assert.False(vm.CambiarFotoCommand.CanExecute(null));
    }

    [Fact]
    public async Task AlEntrar_ElShellTraeLaMiniatura_YSinFotoQuedanLasIniciales()
    {
        _servicio.DescargarAsync(ReductorFotoWpf.LadoMiniatura, Arg.Any<CancellationToken>()).Returns(new byte[] { 9 });
        var shell = new ShellViewModel("AC-04") { Foto = _foto, Fotos = _servicio };

        await shell.CargarAvisosCommand.ExecuteAsync(null);
        Assert.Equal(new byte[] { 9 }, shell.Foto.Miniatura);

        _servicio.DescargarAsync(ReductorFotoWpf.LadoMiniatura, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        await shell.CargarAvisosCommand.ExecuteAsync(null);
        Assert.True(shell.Foto.SinFoto);
    }

    [Fact]
    public void ElAvatar_MuestraLaFotoOLasIniciales() => EnSta(() =>
    {
        var avatar = new Avatar { Iniciales = "LG", Width = 30, Height = 30 };
        Assert.False(avatar.MuestraFoto);

        avatar.Miniatura = Png1x1();
        Assert.True(avatar.MuestraFoto);

        avatar.Miniatura = [1, 2, 3]; // dañada: vuelven las iniciales
        Assert.False(avatar.MuestraFoto);
    });

    private static byte[] Png1x1() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                accion();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        Assert.Null(error);
    }
}
