using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Ventana principal: muestra la vista actual (inicio de sesión, cambio obligatorio de la clave
/// temporal o marco de la sesión) y el aviso de cierre de la sesión (MSG-003, HU-012).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Func<LoginViewModel> _crearLogin;
    private readonly SesionActual _sesionActual;
    private readonly ControlSesion _control;
    private readonly IFabricaPantallas? _pantallas;
    private readonly IServicioSesion? _servicioSesion;
    private readonly SynchronizationContext? _interfaz = SynchronizationContext.Current;

    public MainViewModel(
        Func<LoginViewModel> crearLogin,
        SesionActual sesionActual,
        ControlSesion control,
        IFabricaPantallas? pantallas = null,
        IServicioSesion? servicioSesion = null)
    {
        _crearLogin = crearLogin;
        _sesionActual = sesionActual;
        _control = control;
        _pantallas = pantallas;
        _servicioSesion = servicioSesion;
        _control.SesionTerminada += (_, cierre) => EnLaInterfaz(() => Cierre = cierre);
        _vistaActual = NuevoLogin();
    }

    [ObservableProperty]
    private string _titulo = "SITBIO — Gestión y trazabilidad de equipos biomédicos";

    [ObservableProperty]
    private object _vistaActual;

    /// <summary>Aviso de sesión terminada; mientras exista, los datos de la pantalla quedan ocultos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContenidoVisible))]
    private CierreSesion? _cierre;

    public bool ContenidoVisible => Cierre is null;

    /// <summary>Tecla, clic o rueda del mouse en la ventana.</summary>
    public void RegistrarActividad() => _control.RegistrarActividad();

    [RelayCommand]
    private Task RevisarSesionAsync() => _control.RevisarAsync();

    [RelayCommand]
    private void Reingresar()
    {
        Cierre = null;
        VistaActual = NuevoLogin();
    }

    private LoginViewModel NuevoLogin()
    {
        var login = _crearLogin();
        login.SesionIniciada += (_, sesion) =>
        {
            _sesionActual.Iniciar(sesion);
            _control.Iniciar();
            VistaActual = sesion.DebeCambiarClave && _servicioSesion is not null
                ? CambioObligatorio(sesion.Rol, _servicioSesion)
                : NuevoShell(sesion.Rol);
        };
        return login;
    }

    /// <summary>
    /// CA-HU-013-02 (FLU-001 A1): con clave temporal se muestra UI-002 en lugar del marco, sin
    /// menú ni datos. Al guardar se abre el marco del rol; «Cerrar sesión» revoca el token.
    /// </summary>
    private CambioClaveViewModel CambioObligatorio(string rol, IServicioSesion servicio)
    {
        var cambio = new CambioClaveViewModel(servicio, obligatorio: true);
        cambio.Guardada += (_, _) => VistaActual = NuevoShell(rol, "Contraseña actualizada. Ya puedes usar SITBIO con tu nueva contraseña.");
        cambio.Cancelada += async (_, _) => await CerrarSesionAsync();
        return cambio;
    }

    private ShellViewModel NuevoShell(string rol, string? confirmacion = null)
    {
        Func<CambioClaveViewModel>? cambioVoluntario = _servicioSesion is { } servicio
            ? () => new CambioClaveViewModel(servicio, obligatorio: false)
            : null;
        var shell = new ShellViewModel(rol, _pantallas, cambioVoluntario) { Confirmacion = confirmacion };
        shell.CierreSolicitado += async (_, _) => await CerrarSesionAsync();
        return shell;
    }

    private async Task CerrarSesionAsync()
    {
        await _control.CerrarVoluntarioAsync();
        VistaActual = NuevoLogin();
    }

    private void EnLaInterfaz(Action accion)
    {
        if (_interfaz is null || SynchronizationContext.Current == _interfaz)
        {
            accion();
        }
        else
        {
            _interfaz.Post(_ => accion(), null);
        }
    }
}
