using NSubstitute;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public class LoginViewModelTests
{
    private readonly IServicioAutenticacion _servicio = Substitute.For<IServicioAutenticacion>();
    private readonly LoginViewModel _modelo;

    public LoginViewModelTests()
    {
        _modelo = new LoginViewModel(_servicio) { Correo = "ana@demo.co", Contrasena = "Clave-2026" };
    }

    private void Responder(ResultadoLogin resultado) =>
        _servicio.IniciarSesionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(resultado);

    [Theory]
    [InlineData("", "Clave-2026")]
    [InlineData("sin-arroba", "Clave-2026")]
    [InlineData("ana@demo", "Clave-2026")]
    [InlineData("ana@demo.co", "")]
    public async Task DatosInvalidos_NoLlamaALaApiYMarcaElCampo(string correo, string contrasena)
    {
        _modelo.Correo = correo;
        _modelo.Contrasena = contrasena;

        await _modelo.IniciarSesionCommand.ExecuteAsync(null);

        await _servicio.DidNotReceiveWithAnyArgs().IniciarSesionAsync(default!, default!, default);
        Assert.True(_modelo.TieneErrorCorreo || _modelo.TieneErrorContrasena);
    }

    [Fact]
    public async Task CredencialesValidas_DisparaSesionIniciadaYLimpiaLaContrasena()
    {
        var sesion = new SesionUsuario("token", DateTimeOffset.UtcNow.AddMinutes(30), "AC-02", false);
        Responder(ResultadoLogin.Exitoso(sesion));
        SesionUsuario? recibida = null;
        _modelo.SesionIniciada += (_, s) => recibida = s;

        await _modelo.IniciarSesionCommand.ExecuteAsync(null);

        Assert.Same(sesion, recibida);
        Assert.Equal(string.Empty, _modelo.Contrasena);
        await _servicio.Received(1).IniciarSesionAsync("ana@demo.co", "Clave-2026", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Msg001_ConservaElCorreoVaciaLaContrasenaYPideElFoco()
    {
        Responder(ResultadoLogin.Fallido(FalloLogin.CredencialesIncorrectas));
        var foco = false;
        _modelo.FocoEnContrasenaSolicitado += (_, _) => foco = true;

        await _modelo.IniciarSesionCommand.ExecuteAsync(null);

        Assert.Equal("MSG-001", _modelo.Aviso?.Codigo);
        Assert.Equal(Mensajes.Msg001, _modelo.Aviso?.Mensaje);
        Assert.Equal("ana@demo.co", _modelo.Correo);
        Assert.Equal(string.Empty, _modelo.Contrasena);
        Assert.True(foco);
    }

    [Theory]
    [InlineData(FalloLogin.AccesoNoDisponible, "MSG-002", false)]
    [InlineData(FalloLogin.SinConexion, "MSG-011", true)]
    [InlineData(FalloLogin.DemasiadosIntentos, null, false)]
    public async Task OtrosFallos_MuestranSuAviso(FalloLogin fallo, string? codigo, bool puedeReintentar)
    {
        Responder(ResultadoLogin.Fallido(fallo));

        await _modelo.IniciarSesionCommand.ExecuteAsync(null);

        Assert.True(_modelo.HayAviso);
        Assert.Equal(codigo, _modelo.Aviso!.Codigo);
        Assert.Equal(puedeReintentar, _modelo.Aviso.PuedeReintentar);
    }

    [Fact]
    public async Task NuevoIntento_LimpiaElAvisoAnterior()
    {
        Responder(ResultadoLogin.Fallido(FalloLogin.SinConexion));
        await _modelo.IniciarSesionCommand.ExecuteAsync(null);
        Responder(ResultadoLogin.Exitoso(new SesionUsuario("t", DateTimeOffset.UtcNow, "AC-03", false)));

        await _modelo.IniciarSesionCommand.ExecuteAsync(null);

        Assert.False(_modelo.HayAviso);
    }

    [Fact]
    public void PlanesVigentes_IncluyenConsultorioConSusValores()
    {
        var consultorio = Assert.Single(_modelo.Planes, p => p.Nombre == "Consultorio");

        Assert.Contains("3 usuarios", consultorio.Capacidad);
        Assert.Contains("5 GB", consultorio.Capacidad);
        Assert.Equal(4, _modelo.Planes.Count);
    }
}
