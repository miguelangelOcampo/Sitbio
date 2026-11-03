using NSubstitute;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>Ventana principal en HU-012: cierre voluntario, aviso MSG-003 y regreso al inicio de sesión.</summary>
public class MainViewModelTests
{
    private readonly IServicioAutenticacion _autenticacion = Substitute.For<IServicioAutenticacion>();
    private readonly IServicioSesion _servicioSesion = Substitute.For<IServicioSesion>();
    private readonly SesionActual _sesion = new();
    private readonly Reloj _reloj = new();
    private readonly ControlSesion _control;
    private readonly MainViewModel _principal;

    public MainViewModelTests()
    {
        _autenticacion.IniciarSesionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoLogin.Exitoso(new SesionUsuario("token", DateTimeOffset.UtcNow.AddMinutes(30), "AC-01", false)));
        _control = new ControlSesion(_servicioSesion, _sesion, _reloj, TimeSpan.FromMinutes(30));
        _principal = new MainViewModel(() => new LoginViewModel(_autenticacion), _sesion, _control);
    }

    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private async Task<ShellViewModel> Entrar()
    {
        var login = Assert.IsType<LoginViewModel>(_principal.VistaActual);
        login.Correo = "admin@demo.co";
        login.Contrasena = "clave";
        await login.IniciarSesionCommand.ExecuteAsync(null);
        return Assert.IsType<ShellViewModel>(_principal.VistaActual);
    }

    [Fact]
    public async Task AlIniciarSesion_MuestraElShellConElRol()
    {
        var shell = await Entrar();

        Assert.True(_sesion.Abierta);
        Assert.Equal("Administrador del Sistema", shell.NombreRol);
        Assert.True(_principal.ContenidoVisible);
    }

    [Fact]
    public async Task CA_HU_012_02_CerrarSesion_PideConfirmacionYVuelveAlInicioDeSesion()
    {
        var shell = await Entrar();

        shell.PedirCierreCommand.Execute(null);
        Assert.True(shell.ConfirmandoCierre);
        shell.CancelarCierreCommand.Execute(null);
        Assert.True(_sesion.Abierta);

        shell.PedirCierreCommand.Execute(null);
        shell.ConfirmarCierreCommand.Execute(null);
        await Task.Yield();

        await _servicioSesion.Received(1).CerrarAsync(ServicioSesionHttp.MotivoVoluntario, Arg.Any<CancellationToken>());
        Assert.False(_sesion.Abierta);
        Assert.IsType<LoginViewModel>(_principal.VistaActual);
        Assert.Null(_principal.Cierre);
    }

    [Fact]
    public async Task CA_HU_012_01_SesionTerminada_OcultaLosDatosYReingresarVuelveAlLogin()
    {
        await Entrar();

        _sesion.NotificarRechazo();

        Assert.NotNull(_principal.Cierre);
        Assert.False(_principal.ContenidoVisible);

        _principal.ReingresarCommand.Execute(null);

        Assert.Null(_principal.Cierre);
        Assert.True(_principal.ContenidoVisible);
        Assert.IsType<LoginViewModel>(_principal.VistaActual);
    }

    [Fact]
    public async Task CA_HU_012_01_TrasUnCierreVoluntarioYOtroIngreso_LaInactividadSigueCerrando()
    {
        var shell = await Entrar();
        shell.PedirCierreCommand.Execute(null);
        shell.ConfirmarCierreCommand.Execute(null);
        await Task.Yield();
        await Entrar();

        _reloj.Ahora = _reloj.Ahora.AddMinutes(31);
        await _principal.RevisarSesionCommand.ExecuteAsync(null);

        Assert.Equal("MSG-003", _principal.Cierre?.Codigo);
        await _servicioSesion.Received(1).CerrarAsync(ServicioSesionHttp.MotivoInactividad, Arg.Any<CancellationToken>());
    }
}
