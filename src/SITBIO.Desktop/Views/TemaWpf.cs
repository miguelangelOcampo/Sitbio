using System.Windows;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Intercambia Themes/Light.xaml y Themes/Dark.xaml en los recursos de la aplicación. Las vistas usan
/// <c>DynamicResource</c> para los colores, así que el cambio se ve al instante, sin reiniciar.
/// </summary>
public sealed class TemaWpf : ITemaAplicacion
{
    public void Aplicar(string tema)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var archivo = Temas.Normalizar(tema) == Temas.Oscuro ? "Dark.xaml" : "Light.xaml";
        var diccionarios = app.Resources.MergedDictionaries;
        var actual = diccionarios.FirstOrDefault(d => EsTema(d.Source));
        if (actual?.Source?.OriginalString.EndsWith(archivo, StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var fuente = new Uri($"pack://application:,,,/SITBIO;component/Themes/{archivo}", UriKind.Absolute);
        var nuevo = new ResourceDictionary { Source = fuente };
        if (actual is null)
        {
            diccionarios.Insert(Math.Min(1, diccionarios.Count), nuevo);
        }
        else
        {
            diccionarios[diccionarios.IndexOf(actual)] = nuevo;
        }
    }

    private static bool EsTema(Uri? fuente) =>
        fuente?.OriginalString is { } ruta
        && (ruta.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || ruta.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));
}
