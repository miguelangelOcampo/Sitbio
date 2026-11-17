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

        // D-03: el tema guardado en la estación se aplica antes de abrir la primera ventana.
        var preferencias = new ServicioPreferenciasArchivo();
        var tema = new TemaWpf();
        tema.Aplicar(preferencias.Leer().Tema);
        servicios.AddSingleton<IServicioPreferencias>(preferencias);
        servicios.AddSingleton<ITemaAplicacion>(tema);
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
        servicios.AddTransient<IServicioDocumentosEquipo, ServicioDocumentosEquipoHttp>();
        servicios.AddTransient<IServicioCatalogos, ServicioCatalogosHttp>();
        servicios.AddTransient<CatalogosViewModel>();
        servicios.AddTransient<IServicioConfiguracionCampos, ServicioConfiguracionCamposHttp>();
        servicios.AddTransient<CamposDinamicosViewModel>();
        servicios.AddTransient<EquiposViewModel>();
        servicios.AddTransient<IServicioTiquetes, ServicioTiquetesHttp>();
        servicios.AddTransient<ReportarFallaViewModel>();
        servicios.AddTransient<SeguimientoViewModel>();
        servicios.AddTransient<IServicioOrdenes, ServicioOrdenesHttp>();
        servicios.AddTransient<BandejaViewModel>();
        servicios.AddTransient<OrdenesViewModel>();
        servicios.AddTransient<IServicioAnalitica, ServicioAnaliticaHttp>();
        servicios.AddTransient<AnaliticaViewModel>();
        servicios.AddTransient<IServicioMantenimiento, ServicioMantenimientoHttp>();
        servicios.AddTransient<MantenimientosViewModel>();
        servicios.AddTransient<CentroAlertasViewModel>();
        servicios.AddTransient<IServicioReportes, ServicioReportesHttp>();
        servicios.AddSingleton<IDialogoArchivos, DialogoArchivosWpf>();
        servicios.AddTransient<ReportesViewModel>();
        servicios.AddTransient<PanelViewModel>();
        servicios.AddTransient<PerfilViewModel>();
        servicios.AddTransient<ExportacionViewModel>();
        servicios.AddTransient<IServicioSesion, ServicioSesionHttp>();
        servicios.AddTransient<IServicioBusqueda, ServicioBusquedaHttp>();
        servicios.AddSingleton(TimeProvider.System);
        servicios.AddSingleton<ControlSesion>(sp => new ControlSesion(
            sp.GetRequiredService<IServicioSesion>(), sp.GetRequiredService<SesionActual>(), sp.GetRequiredService<TimeProvider>()));
        servicios.AddTransient<IServicioSolicitudesServicio, ServicioSolicitudesServicioHttp>();
        servicios.AddTransient<SolicitudServicioViewModel>();
        servicios.AddTransient<LoginViewModel>(sp => new LoginViewModel(
            sp.GetRequiredService<IServicioAutenticacion>(), () => sp.GetRequiredService<SolicitudServicioViewModel>()));
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
