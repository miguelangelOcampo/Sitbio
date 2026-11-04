using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _revision = new() { Interval = ControlSesion.IntervaloRevision };

    public MainWindow()
    {
        InitializeComponent();

        // Actividad para el cierre por inactividad (HU-012): teclas, clics y rueda del mouse.
        PreviewKeyDown += (_, _) => Modelo?.RegistrarActividad();
        PreviewMouseDown += (_, _) => Modelo?.RegistrarActividad();
        PreviewMouseWheel += (_, _) => Modelo?.RegistrarActividad();

        _revision.Tick += (_, _) => Modelo?.RevisarSesionCommand.Execute(null);
        Loaded += (_, _) => _revision.Start();
        Closed += (_, _) => _revision.Stop();
    }

    private MainViewModel? Modelo => DataContext as MainViewModel;
}
