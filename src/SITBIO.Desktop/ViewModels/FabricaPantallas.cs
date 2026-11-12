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
