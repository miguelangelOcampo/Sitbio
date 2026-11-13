using System.Net.Http;
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
        servicios.AddSingleton<SesionActual>();
        servicios.AddTransient<ManejadorToken>();
        servicios.AddHttpClient<IServicioAutenticacion, ServicioAutenticacionHttp>(ConfigurarCliente);

        // Los servicios de cada historia usan ClienteApi: el token viaja en cada solicitud.
        servicios.AddHttpClient<ClienteApi>(ConfigurarCliente).AddHttpMessageHandler<ManejadorToken>();
        servicios.AddTransient<IServicioUsuarios, ServicioUsuariosHttp>();
        servicios.AddSingleton<IPortapapeles, PortapapelesWpf>();
        servicios.AddSingleton<IFabricaPantallas, FabricaPantallas>();
        servicios.AddTransient<UsuariosViewModel>();
        servicios.AddTransient<IServicioBitacora, ServicioBitacoraHttp>();
        servicios.AddTransient<BitacoraViewModel>();
        servicios.AddTransient<IServicioInstitucion, ServicioInstitucionHttp>();
        servicios.AddTransient<MiInstitucionViewModel>();
        servicios.AddTransient<IServicioEquipos, ServicioEquiposHttp>();
        servicios.AddTransient<IServicioCatalogos, ServicioCatalogosHttp>();
        servicios.AddTransient<CatalogosViewModel>();
        servicios.AddTransient<IServicioConfiguracionCampos, ServicioConfiguracionCamposHttp>();
        servicios.AddTransient<CamposDinamicosViewModel>();
        servicios.AddTransient<EquiposViewModel>();
        servicios.AddTransient<IServicioTiquetes, ServicioTiquetesHttp>();
        servicios.AddTransient<ReportarFallaViewModel>();
        servicios.AddTransient<IServicioSesion, ServicioSesionHttp>();
        servicios.AddSingleton(TimeProvider.System);
        servicios.AddSingleton<ControlSesion>(sp => new ControlSesion(
            sp.GetRequiredService<IServicioSesion>(), sp.GetRequiredService<SesionActual>(), sp.GetRequiredService<TimeProvider>()));
        servicios.AddTransient<LoginViewModel>();
        servicios.AddSingleton<Func<LoginViewModel>>(sp => () => sp.GetRequiredService<LoginViewModel>());
        servicios.AddSingleton<MainViewModel>();
        _servicios = servicios.BuildServiceProvider();

        MainWindow = new MainWindow { DataContext = _servicios.GetRequiredService<MainViewModel>() };
        MainWindow.Show();
    }

    private static void ConfigurarCliente(HttpClient cliente)
    {
        cliente.BaseAddress = ConfiguracionApi.UrlBase();
        cliente.Timeout = TimeSpan.FromSeconds(15);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _servicios?.Dispose();
        base.OnExit(e);
    }
}
