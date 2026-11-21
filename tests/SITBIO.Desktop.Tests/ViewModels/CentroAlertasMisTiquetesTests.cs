using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// J-09 (HU-017): la campana avisa de los cambios de estado de los tiquetes que reportó quien está en la sesión.
/// Lo nuevo se cuenta contra la última revisión de ese usuario en esta estación; abrir el panel la guarda y
/// «Ver tiquete» lleva a UI-010. Quien no reporta no consulta sus cambios.
/// </summary>
public class CentroAlertasMisTiquetesTests
{
    private static readonly EquiposPrueba.Reloj Reloj = new();
    private static readonly DateTimeOffset Ahora = Reloj.GetUtcNow();

    private readonly IServicioMantenimiento _mantenimiento = Substitute.For<IServicioMantenimiento>();
    private readonly IServicioTiquetes _tiquetes = Substitute.For<IServicioTiquetes>();
    private readonly PreferenciasEnMemoria _preferencias = new();
    private readonly Guid _laura = Guid.NewGuid();

    private static string B64(string texto) => Convert.ToBase64String(Encoding.UTF8.GetBytes(texto)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static SesionActual Sesion(string rol, Guid usuario)
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario($"{B64("{}")}.{B64($$"""{"sub":"{{usuario}}"}""")}.firma", Ahora.AddMinutes(30), rol, false, "Laura", "Gómez"));
        return sesion;
    }

    private static CambioMiTiquete Cambio(string codigo, string nuevo, TimeSpan hace, string? comentario = null, string usuario = "Carlos Ruiz") =>
        new(codigo, "PL-001", "Monitor", EstadosTiquete.Recibido, nuevo, comentario, Ahora - hace, usuario);

    private CentroAlertasViewModel Centro(string rol, Guid? usuario = null, params CambioMiTiquete[] cambios)
    {
        _mantenimiento.AlertasAsync(Arg.Any<CancellationToken>()).Returns(new ResumenAlertas(15, [], 0));
        _tiquetes.MisCambiosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(cambios);
        return new CentroAlertasViewModel(_mantenimiento, _tiquetes, Sesion(rol, usuario ?? _laura), _preferencias, Reloj);
    }

    [Fact]
    public async Task PersonalClinico_VeSoloSusTiquetes_SinVencimientos()
    {
        var vm = Centro("AC-04", null,
            Cambio("TQT-000003", EstadosTiquete.EnEjecucion, TimeSpan.FromMinutes(12)),
            Cambio("TQT-000002", EstadosTiquete.Asignado, TimeSpan.FromHours(3), "Se asignó a Carlos Ruiz", "Andrés Rojas"));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Notificaciones", false, true), (vm.Titulo, vm.VeVencimientos, vm.VeMisTiquetes));
        await _mantenimiento.DidNotReceiveWithAnyArgs().AlertasAsync(default);
        await _tiquetes.Received(1).MisCambiosAsync(Ahora.AddDays(-CentroAlertasViewModel.DiasCambios), Arg.Any<CancellationToken>());
        Assert.Equal(["TQT-000003 · Monitor", "TQT-000002 · Monitor"], vm.Cambios.Select(c => c.Titulo));
        Assert.All(vm.Cambios, c => Assert.True(c.Nuevo));
        Assert.Equal((2, 2, true), (vm.CambiosSinLeer, vm.Pendientes, vm.HayCambios));
        Assert.False(vm.HayVencimientosPendientes);
        Assert.Equal(("El técnico empezó a atender la falla.", "Carlos Ruiz · Hace 12 min"), (vm.Cambios[0].Texto, vm.Cambios[0].Detalle));
        Assert.Equal("Se asignó a Carlos Ruiz", vm.Cambios[1].Comentario);
    }

    [Fact]
    public async Task SoloCuentaLoPosteriorALaUltimaRevisionDelUsuario()
    {
        _preferencias.Guardar(new Preferencias().ConRevision(_laura, Ahora.AddHours(-1)));
        var vm = Centro("AC-04", null,
            Cambio("TQT-000003", EstadosTiquete.Cerrado, TimeSpan.FromMinutes(5)),
            Cambio("TQT-000002", EstadosTiquete.Asignado, TimeSpan.FromHours(2)));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal([true, false], vm.Cambios.Select(c => c.Nuevo));
        Assert.Equal(1, vm.CambiosSinLeer);
    }

    [Fact]
    public async Task LaRevisionDeOtroUsuarioDeLaEstacion_NoMarcaComoVistoLoMio()
    {
        // Aislamiento en una estación compartida: lo que revisó Andrés no cuenta para Laura.
        _preferencias.Guardar(new Preferencias().ConRevision(Guid.NewGuid(), Ahora));
        var vm = Centro("AC-04", null, Cambio("TQT-000003", EstadosTiquete.Asignado, TimeSpan.FromMinutes(5)));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.CambiosSinLeer);
    }

    [Fact]
    public async Task AbrirElPanel_GuardaComoVistoElCambioMasReciente_YQuitaElNumero()
    {
        var vm = Centro("AC-02", null,
            Cambio("TQT-000003", EstadosTiquete.EnEjecucion, TimeSpan.FromMinutes(12)),
            Cambio("TQT-000002", EstadosTiquete.Asignado, TimeSpan.FromHours(3)));
        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CambiosSinLeer);

        vm.AlternarCommand.Execute(null);
        await vm.CargarCommand.ExecutionTask!;

        Assert.Equal(0, vm.CambiosSinLeer);
        Assert.Equal(Ahora.AddMinutes(-12), _preferencias.Leer().RevisionDe(_laura));

        // La siguiente vez nada es nuevo para Laura.
        var otra = Centro("AC-02", null, Cambio("TQT-000003", EstadosTiquete.EnEjecucion, TimeSpan.FromMinutes(12)));
        await otra.CargarCommand.ExecuteAsync(null);
        Assert.Equal(0, otra.CambiosSinLeer);
    }

    [Fact]
    public async Task VerTiquete_AbreUi010ConElCodigoYCierraElPanel()
    {
        var vm = Centro("AC-04", null, Cambio("TQT-000003", EstadosTiquete.Asignado, TimeSpan.FromMinutes(5)));
        await vm.CargarCommand.ExecuteAsync(null);
        vm.AlternarCommand.Execute(null);
        SolicitudNavegacion? pedida = null;
        vm.NavegacionSolicitada += (_, s) => pedida = s;

        vm.AbrirCambioCommand.Execute(vm.Cambios[0]);

        Assert.Equal(new SolicitudNavegacion(SeguimientoViewModel.IdPantalla, "TQT-000003"), pedida);
        Assert.False(vm.Abierto);
    }

    [Fact]
    public async Task SiLaConsultaFalla_LoAvisaSinNumero()
    {
        var vm = Centro("AC-04");
        _tiquetes.MisCambiosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), true));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011"), (vm.AvisoCambios!.Titulo, vm.AvisoCambios.Codigo));
        Assert.Equal((0, false), (vm.Pendientes, vm.SinCambios));
    }

    [Fact]
    public async Task SinCambios_LoDice()
    {
        var vm = Centro("AC-04");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinCambios);
        Assert.Equal(0, vm.Pendientes);
    }

    [Fact]
    public async Task ElTecnicoNoReporta_NoConsultaCambiosDeTiquetes()
    {
        var vm = Centro("AC-03");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Centro de alertas", true, false), (vm.Titulo, vm.VeVencimientos, vm.VeMisTiquetes));
        await _tiquetes.DidNotReceiveWithAnyArgs().MisCambiosAsync(default, default);
    }

    [Theory]
    [InlineData(0.5, "Hace un momento")]
    [InlineData(5, "Hace 5 min")]
    [InlineData(180, "Hace 3 h")]
    [InlineData(1500, "Ayer")]
    [InlineData(5760, "Hace 4 días")]
    public void Relativo_EnPalabras(double minutos, string esperado) =>
        Assert.Equal(esperado, FilaCambioTiquete.Relativo(Ahora.AddMinutes(-minutos), Ahora));

    private sealed class PreferenciasEnMemoria : IServicioPreferencias
    {
        private Preferencias _actuales = new();

        public Preferencias Leer() => _actuales;

        public void Guardar(Preferencias preferencias) => _actuales = preferencias;
    }
}
