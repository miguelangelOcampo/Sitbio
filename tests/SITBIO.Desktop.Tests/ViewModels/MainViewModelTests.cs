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

    // ---------- HU-013: clave temporal (FLU-001 A1) ----------

    private async Task<MainViewModel> EntrarConClaveTemporal(bool temporal = true)
    {
        _autenticacion.IniciarSesionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoLogin.Exitoso(new SesionUsuario("token", DateTimeOffset.UtcNow.AddMinutes(30), "AC-03", temporal)));
        var principal = new MainViewModel(() => new LoginViewModel(_autenticacion), _sesion, _control, servicioSesion: _servicioSesion);
        var login = Assert.IsType<LoginViewModel>(principal.VistaActual);
        login.Correo = "tecnico@demo.co";
        login.Contrasena = "temporal";
        await login.IniciarSesionCommand.ExecuteAsync(null);
        return principal;
    }

    private static async Task Guardar(CambioClaveViewModel cambio)
    {
        cambio.Actual = "Tmp#7kPq2xWz9a";
        cambio.Nueva = cambio.Confirmacion = "Monitor#2026uci";
        await cambio.GuardarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task CA_HU_013_02_ConClaveTemporal_MuestraUI002SinElMarco()
    {
        var principal = await EntrarConClaveTemporal();

        var cambio = Assert.IsType<CambioClaveViewModel>(principal.VistaActual);
        Assert.True(cambio.Obligatorio);
        Assert.True(_sesion.Abierta);
    }

    [Fact]
    public async Task CA_HU_013_02_AlGuardar_AbreElMarcoDelRolConLaConfirmacion()
    {
        var principal = await EntrarConClaveTemporal();

        await Guardar(Assert.IsType<CambioClaveViewModel>(principal.VistaActual));

        var shell = Assert.IsType<ShellViewModel>(principal.VistaActual);
        Assert.Equal("AC-03", shell.Rol);
        Assert.StartsWith("Contraseña actualizada", shell.Confirmacion);
    }

    [Fact]
    public async Task CA_HU_013_02_CerrarSesionDesdeUI002_RevocaYVuelveAlInicio()
    {
        var principal = await EntrarConClaveTemporal();

        Assert.IsType<CambioClaveViewModel>(principal.VistaActual).CancelarCommand.Execute(null);
        await Task.Yield();

        await _servicioSesion.Received(1).CerrarAsync(ServicioSesionHttp.MotivoVoluntario, Arg.Any<CancellationToken>());
        Assert.False(_sesion.Abierta);
        Assert.IsType<LoginViewModel>(principal.VistaActual);
    }

    [Fact]
    public async Task CambioVoluntario_DesdeElMarco_SeAbreYSeCierraConConfirmacion()
    {
        var principal = await EntrarConClaveTemporal(temporal: false);
        var shell = Assert.IsType<ShellViewModel>(principal.VistaActual);
        Assert.True(shell.PuedeCambiarClave);

        shell.AbrirCambioClaveCommand.Execute(null);
        var cambio = shell.CambioClave!;
        Assert.False(cambio.Obligatorio);
        cambio.CancelarCommand.Execute(null);
        Assert.Null(shell.CambioClave);

        shell.AbrirCambioClaveCommand.Execute(null);
        await Guardar(shell.CambioClave!);

        Assert.Null(shell.CambioClave);
        Assert.Contains("Las demás sesiones abiertas con tu cuenta se cerraron", shell.Confirmacion);
        shell.CerrarConfirmacionCommand.Execute(null);
        Assert.Null(shell.Confirmacion);
    }

    [Fact]
    public async Task SinServicioDeSesion_NoOfreceElCambioVoluntario()
    {
        var shell = await Entrar();

        Assert.False(shell.PuedeCambiarClave);
    }
}
