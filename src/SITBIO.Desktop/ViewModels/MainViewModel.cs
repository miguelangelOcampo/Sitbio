using CommunityToolkit.Mvvm.ComponentModel;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Ventana principal: muestra la vista actual (inicio de sesión o sesión abierta).</summary>
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(LoginViewModel login)
    {
        _vistaActual = login;
        login.SesionIniciada += (_, sesion) => VistaActual = new SesionIniciadaViewModel(sesion);
    }

    [ObservableProperty]
    private string _titulo = "SITBIO — Gestión y trazabilidad de equipos biomédicos";

    [ObservableProperty]
    private object _vistaActual;
}
