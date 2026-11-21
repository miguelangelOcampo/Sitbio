using System.Windows;
using Microsoft.Win32;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Intercambia Themes/Light.xaml y Themes/Dark.xaml en los recursos de la aplicación. Las vistas usan
/// <c>DynamicResource</c> para los colores, así que el cambio se ve al instante, sin reiniciar. Con «Sistema» (J-10)
/// lee <c>AppsUseLightTheme</c> del registro y vuelve a aplicarse cuando Windows cambia de modo.
/// </summary>
public sealed class TemaWpf : ITemaAplicacion
{
    private const string ClavePersonalizar = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private string _tema = Temas.Sistema;
    private bool _escuchando;

    /// <summary>Modo de aplicaciones de Windows; sin el valor (versiones antiguas o sin permiso), claro.</summary>
    public static bool SistemaEnClaro()
    {
        try
        {
            using var clave = Registry.CurrentUser.OpenSubKey(ClavePersonalizar);
            return clave?.GetValue("AppsUseLightTheme") is not int valor || valor != 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }

    public void Aplicar(string tema)
    {
        _tema = Temas.Normalizar(tema);
        if (_tema == Temas.Sistema && !_escuchando)
        {
            _escuchando = true;
            SystemEvents.UserPreferenceChanged += AlCambiarWindows;
        }

        Pintar();
    }

    private void AlCambiarWindows(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_tema == Temas.Sistema && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(Pintar);
        }
    }

    private void Pintar()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var archivo = Temas.Resolver(_tema, SistemaEnClaro()) == Temas.Oscuro ? "Dark.xaml" : "Light.xaml";
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
