using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using Sitbio.Desktop.Views;

namespace Sitbio.Desktop;

/// <summary>Punto de composición del cliente: registra servicios y abre la ventana principal.</summary>
public partial class App : Application
{
    private ServiceProvider? _servicios;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var servicios = new ServiceCollection();
        servicios.AddHttpClient<IServicioAutenticacion, ServicioAutenticacionHttp>(cliente =>
        {
            cliente.BaseAddress = ConfiguracionApi.UrlBase();
            cliente.Timeout = TimeSpan.FromSeconds(15);
        });
        servicios.AddTransient<LoginViewModel>();
        servicios.AddSingleton<MainViewModel>();
        _servicios = servicios.BuildServiceProvider();

        MainWindow = new MainWindow { DataContext = _servicios.GetRequiredService<MainViewModel>() };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _servicios?.Dispose();
        base.OnExit(e);
    }
}
