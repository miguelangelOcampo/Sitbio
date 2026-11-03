using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Ventana principal: muestra la vista actual (inicio de sesión o marco de la sesión) y el
/// aviso de cierre de la sesión (MSG-003, HU-012).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Func<LoginViewModel> _crearLogin;
    private readonly SesionActual _sesionActual;
    private readonly ControlSesion _control;
    private readonly IFabricaPantallas? _pantallas;
    private readonly SynchronizationContext? _interfaz = SynchronizationContext.Current;

    public MainViewModel(Func<LoginViewModel> crearLogin, SesionActual sesionActual, ControlSesion control, IFabricaPantallas? pantallas = null)
    {
        _crearLogin = crearLogin;
        _sesionActual = sesionActual;
        _control = control;
        _pantallas = pantallas;
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
            var shell = new ShellViewModel(sesion.Rol, _pantallas);
            shell.CierreSolicitado += async (_, _) =>
            {
                await _control.CerrarVoluntarioAsync();
                VistaActual = NuevoLogin();
            };
            VistaActual = shell;
        };
        return login;
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
