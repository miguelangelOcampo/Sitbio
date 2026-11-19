using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Comportamiento adjunto para las tablas dentro de una página con desplazamiento: el DataGrid se queda con
/// la rueda del mouse aunque no tenga nada que desplazar, y la página no baja. Con <c>Activa=True</c>, la rueda
/// pasa al ScrollViewer de la página cuando la tabla no tiene desplazamiento propio o ya llegó a su borde.
/// Se aplica en el estilo <c>OpenGrid</c>, así cubre todas las tablas.
/// </summary>
public static class RuedaHaciaPadre
{
    public static readonly DependencyProperty ActivaProperty = DependencyProperty.RegisterAttached(
        "Activa", typeof(bool), typeof(RuedaHaciaPadre), new PropertyMetadata(false, AlCambiar));

    public static bool GetActiva(DependencyObject elemento) => (bool)elemento.GetValue(ActivaProperty);

    public static void SetActiva(DependencyObject elemento, bool valor) => elemento.SetValue(ActivaProperty, valor);

    /// <summary>
    /// La tabla reenvía la rueda si no puede desplazarse en esa dirección: sin desplazamiento propio, arriba
    /// del todo al subir (delta positivo) o abajo del todo al bajar (delta negativo).
    /// </summary>
    public static bool DebeReenviar(double desplazable, double posicion, int delta)
    {
        const double Tolerancia = 0.5;
        if (desplazable <= Tolerancia)
        {
            return true;
        }

        return delta > 0 ? posicion <= Tolerancia : posicion >= desplazable - Tolerancia;
    }

    private static void AlCambiar(DependencyObject elemento, DependencyPropertyChangedEventArgs e)
    {
        if (elemento is not UIElement control)
        {
            return;
        }

        control.PreviewMouseWheel -= AlGirarRueda;
        if (e.NewValue is true)
        {
            control.PreviewMouseWheel += AlGirarRueda;
        }
    }

    private static void AlGirarRueda(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject control)
        {
            return;
        }

        var propio = Descendiente<ScrollViewer>(control);
        if (propio is not null && !DebeReenviar(propio.ScrollableHeight, propio.VerticalOffset, e.Delta))
        {
            return;
        }

        if (VisualTreeHelper.GetParent(control) is not UIElement padre)
        {
            return;
        }

        e.Handled = true;
        padre.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = control,
        });
    }

    private static T? Descendiente<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T encontrado)
            {
                return encontrado;
            }

            if (Descendiente<T>(hijo) is { } profundo)
            {
                return profundo;
            }
        }

        return null;
    }
}
