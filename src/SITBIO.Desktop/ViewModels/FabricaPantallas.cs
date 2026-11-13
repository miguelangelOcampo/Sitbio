using Microsoft.Extensions.DependencyInjection;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Crea el ViewModel de la pantalla de cada opción del menú (id UI-###); null = aún no existe.</summary>
public interface IFabricaPantallas
{
    object? Crear(string idOpcion);
}

/// <summary>Pantallas disponibles. Cada historia agrega aquí la suya.</summary>
public sealed class FabricaPantallas(IServiceProvider servicios) : IFabricaPantallas
{
    public object? Crear(string idOpcion)
    {
        switch (idOpcion)
        {
            case "UI-004":
                var usuarios = servicios.GetRequiredService<UsuariosViewModel>();
                usuarios.CargarCommand.Execute(null);
                return usuarios;
            case "UI-005":
                var equipos = servicios.GetRequiredService<EquiposViewModel>();
                equipos.CargarCommand.Execute(null);
                return equipos;
            // UI-019 Configuración: «Mi institución» solo para AC-01 (HU-024); «Catálogos» (UI-020, HU-025) y
            // «Campos dinámicos» (UI-018, HU-006) para AC-01 y AC-02. Campos dinámicos carga al abrir su pestaña.
            // UI-009 Reportar falla (HU-003, HU-018): pantalla de inicio de AC-04; también la usa AC-02.
            case "UI-009":
                var reporte = servicios.GetRequiredService<ReportarFallaViewModel>();
                reporte.CargarCommand.Execute(null);
                return reporte;
            // UI-010 Seguimiento de tiquetes (HU-017): AC-01 a AC-04 y AC-07; para AC-04 es «Mis tiquetes».
            case SeguimientoViewModel.IdPantalla:
                var seguimiento = servicios.GetRequiredService<SeguimientoViewModel>();
                seguimiento.CargarCommand.Execute(null);
                return seguimiento;
            // UI-011 Bandeja de tiquetes y asignación (HU-004, HU-018): solo AC-02.
            case BandejaViewModel.IdPantalla:
                var bandeja = servicios.GetRequiredService<BandejaViewModel>();
                bandeja.CargarCommand.Execute(null);
                return bandeja;
            // UI-015 Mantenimientos (HU-019): planes preventivos; crea y edita AC-02, consultan AC-01, AC-03, AC-05 y AC-07.
            case MantenimientosViewModel.IdPantalla:
                var mantenimientos = servicios.GetRequiredService<MantenimientosViewModel>();
                mantenimientos.CargarCommand.Execute(null);
                return mantenimientos;
            // UI-016 Centro de alertas (HU-020): lo crea el marco al iniciar la sesión de AC-02 y AC-03.
            case CentroAlertasViewModel.IdPantalla:
                var alertas = servicios.GetRequiredService<CentroAlertasViewModel>();
                alertas.CargarCommand.Execute(null);
                return alertas;
            case "UI-019":
                var catalogos = servicios.GetRequiredService<CatalogosViewModel>();
                catalogos.CargarCommand.Execute(null);
                MiInstitucionViewModel? institucion = null;
                if (servicios.GetService<SesionActual>()?.Sesion?.Rol == "AC-01")
                {
                    institucion = servicios.GetRequiredService<MiInstitucionViewModel>();
                    institucion.CargarCommand.Execute(null);
                }

                return new ConfiguracionViewModel(institucion, catalogos, servicios.GetRequiredService<CamposDinamicosViewModel>());
            case "UI-024":
                var bitacora = servicios.GetRequiredService<BitacoraViewModel>();
                bitacora.CargarCommand.Execute(null);
                return bitacora;
            default:
                return null;
        }
    }
}
