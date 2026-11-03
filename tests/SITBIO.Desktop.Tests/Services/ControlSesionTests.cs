using NSubstitute;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>HU-012 en el cliente: inactividad (CA-HU-012-01), renovación y cierre voluntario (CA-HU-012-02).</summary>
public class ControlSesionTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 3, 14, 0, 0, TimeSpan.Zero);

    private readonly IServicioSesion _servicio = Substitute.For<IServicioSesion>();
    private readonly SesionActual _sesion = new();
    private readonly Reloj _reloj = new();
    private readonly ControlSesion _control;
    private readonly List<CierreSesion> _cierres = [];

    public ControlSesionTests()
    {
        _servicio.RenovarAsync(Arg.Any<CancellationToken>()).Returns(ResultadoRenovacion.SinConexion);
        _control = new ControlSesion(_servicio, _sesion, _reloj, TimeSpan.FromMinutes(ControlSesion.MinutosInactividad));
        _control.SesionTerminada += (_, cierre) => _cierres.Add(cierre);
        _sesion.Iniciar(Sesion("token-1", Inicio.AddMinutes(30)));
        _control.Iniciar();
    }

    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = Inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private static SesionUsuario Sesion(string token, DateTimeOffset expira) => new(token, expira, "AC-02", false);

    private void Avanzar(int minutos) => _reloj.Ahora = _reloj.Ahora.AddMinutes(minutos);

    [Fact]
    public async Task CA_HU_012_01_ConMenosDe30MinutosSinActividad_NoCierra()
    {
        _servicio.RenovarAsync(Arg.Any<CancellationToken>()).Returns(new ResultadoRenovacion(Sesion("token-2", Inicio.AddMinutes(59)), false));
        Avanzar(29);

        await _control.RevisarAsync();

        Assert.True(_sesion.Abierta);
        Assert.Empty(_cierres);
        await _servicio.DidNotReceiveWithAnyArgs().CerrarAsync(default!, default);
    }

    [Fact]
    public async Task CA_HU_012_01_A_Los30MinutosSinActividad_RevocaElTokenYMuestraMsg003()
    {
        Avanzar(30);

        await _control.RevisarAsync();

        await _servicio.Received(1).CerrarAsync(ServicioSesionHttp.MotivoInactividad, Arg.Any<CancellationToken>());
        Assert.False(_sesion.Abierta);
        var cierre = Assert.Single(_cierres);
        Assert.Equal("MSG-003", cierre.Codigo);
        Assert.Equal(Mensajes.Msg003, cierre.Mensaje);
    }

    [Fact]
    public async Task CA_HU_012_01_LaActividadReiniciaElConteo()
    {
        Avanzar(20);
        _control.RegistrarActividad();
        Avanzar(20);

        await _control.RevisarAsync();

        Assert.True(_sesion.Abierta);
        Assert.Empty(_cierres);
    }

    [Fact]
    public async Task CA_HU_012_01_RevisarDosVeces_CierraUnaSolaVez()
    {
        Avanzar(31);

        await _control.RevisarAsync();
        await _control.RevisarAsync();

        Assert.Single(_cierres);
        await _servicio.Received(1).CerrarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activo_ConElTokenPorVencer_LoRenueva()
    {
        _servicio.RenovarAsync(Arg.Any<CancellationToken>()).Returns(new ResultadoRenovacion(Sesion("token-2", Inicio.AddMinutes(56)), false));
        Avanzar(26);
        _control.RegistrarActividad();

        await _control.RevisarAsync();

        Assert.Equal("token-2", _sesion.Sesion?.Token);
        Assert.Empty(_cierres);
    }

    [Fact]
    public async Task ConElTokenLejosDeVencer_NoRenueva()
    {
        Avanzar(10);
        _control.RegistrarActividad();

        await _control.RevisarAsync();

        await _servicio.DidNotReceiveWithAnyArgs().RenovarAsync(default);
        Assert.Equal("token-1", _sesion.Sesion?.Token);
    }

    [Fact]
    public async Task RenovacionRechazada_TerminaLaSesion()
    {
        _servicio.RenovarAsync(Arg.Any<CancellationToken>()).Returns(new ResultadoRenovacion(null, Rechazada: true));
        Avanzar(27);
        _control.RegistrarActividad();

        await _control.RevisarAsync();

        Assert.False(_sesion.Abierta);
        Assert.Same(CierreSesion.Rechazo, Assert.Single(_cierres));
    }

    [Fact]
    public async Task RenovacionSinConexion_ConservaLaSesionYReintentaDespues()
    {
        _servicio.RenovarAsync(Arg.Any<CancellationToken>()).Returns(ResultadoRenovacion.SinConexion);
        Avanzar(27);
        _control.RegistrarActividad();

        await _control.RevisarAsync();

        Assert.True(_sesion.Abierta);
        Assert.Empty(_cierres);
    }

    [Fact]
    public void TokenRechazadoPorLaApi_TerminaLaSesion()
    {
        _sesion.NotificarRechazo();

        Assert.False(_sesion.Abierta);
        Assert.Same(CierreSesion.Rechazo, Assert.Single(_cierres));
    }

    [Fact]
    public async Task CA_HU_012_02_CierreVoluntario_RevocaYOlvidaElTokenSinAvisoDeExpiracion()
    {
        await _control.CerrarVoluntarioAsync();

        await _servicio.Received(1).CerrarAsync(ServicioSesionHttp.MotivoVoluntario, Arg.Any<CancellationToken>());
        Assert.False(_sesion.Abierta);
        Assert.Empty(_cierres);
    }

    [Fact]
    public async Task UnRechazoDuranteElCierre_NoMuestraUnSegundoAviso()
    {
        _servicio.When(s => s.CerrarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(_ => _sesion.NotificarRechazo());
        Avanzar(30);

        await _control.RevisarAsync();

        Assert.Equal("MSG-003", Assert.Single(_cierres).Codigo);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("2", 2)]
    [InlineData("0", 30)]
    [InlineData("45", 30)]
    [InlineData("abc", 30)]
    public void LimiteDesdeEntorno_SoloAceptaDe1A30Minutos(string? valor, int esperado)
    {
        var anterior = Environment.GetEnvironmentVariable(ControlSesion.VariableMinutosInactividad);
        try
        {
            Environment.SetEnvironmentVariable(ControlSesion.VariableMinutosInactividad, valor);

            Assert.Equal(TimeSpan.FromMinutes(esperado), ControlSesion.LimiteDesdeEntorno());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ControlSesion.VariableMinutosInactividad, anterior);
        }
    }
}
