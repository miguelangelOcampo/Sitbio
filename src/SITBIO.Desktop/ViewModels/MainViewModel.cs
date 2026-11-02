using CommunityToolkit.Mvvm.ComponentModel;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Ventana principal: muestra la vista actual (inicio de sesión o marco de la sesión).</summary>
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(LoginViewModel login, SesionActual sesionActual, IFabricaPantallas? pantallas = null)
    {
        _vistaActual = login;
        login.SesionIniciada += (_, sesion) =>
        {
            sesionActual.Iniciar(sesion);
            VistaActual = new ShellViewModel(sesion.Rol, pantallas);
        };
    }

    [ObservableProperty]
    private string _titulo = "SITBIO — Gestión y trazabilidad de equipos biomédicos";

    [ObservableProperty]
    private object _vistaActual;
}
