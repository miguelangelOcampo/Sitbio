using Microsoft.Extensions.DependencyInjection;

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
            default:
                return null;
        }
    }
}
