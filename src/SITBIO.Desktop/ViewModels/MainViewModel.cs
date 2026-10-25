using CommunityToolkit.Mvvm.ComponentModel;

namespace Sitbio.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titulo = "SITBIO";

    [ObservableProperty]
    private string _mensaje = "Estructura base lista. La primera pantalla es el inicio de sesión (UI-001, HU-001).";
}
