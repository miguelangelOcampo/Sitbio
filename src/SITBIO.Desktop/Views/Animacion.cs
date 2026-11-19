using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Animaciones del prototipo (H-02): la pantalla nueva y los diálogos y menús aparecen con un desvanecido corto y un
/// desplazamiento de 8 px hacia arriba. Si Windows tiene «Reducir animaciones» (o desactivados los efectos de
/// animación), <see cref="SystemParameters.ClientAreaAnimation"/> es false y todo aparece sin animar.
/// </summary>
public static class Animacion
{
    public static readonly Duration Duracion = new(TimeSpan.FromMilliseconds(180));
    public const double Desplazamiento = 8;

    /// <summary>Solo para pruebas: fuerza el valor de <see cref="Activas"/>; null = lo que diga Windows.</summary>
    public static bool? Forzar { get; set; }

    public static bool Activas => Forzar ?? (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast);

    /// <summary>El elemento aparece animado cada vez que se carga o se vuelve visible (diálogos, menús).</summary>
    public static readonly DependencyProperty EntradaProperty = DependencyProperty.RegisterAttached(
        "Entrada", typeof(bool), typeof(Animacion), new PropertyMetadata(false, AlCambiarEntrada));

    public static bool GetEntrada(DependencyObject elemento) => (bool)elemento.GetValue(EntradaProperty);

    public static void SetEntrada(DependencyObject elemento, bool valor) => elemento.SetValue(EntradaProperty, valor);

    /// <summary>El contenido de un ContentControl aparece animado cada vez que cambia (la pantalla del shell).</summary>
    public static readonly DependencyProperty CambioContenidoProperty = DependencyProperty.RegisterAttached(
        "CambioContenido", typeof(bool), typeof(Animacion), new PropertyMetadata(false, AlCambiarCambioContenido));

    public static bool GetCambioContenido(DependencyObject elemento) => (bool)elemento.GetValue(CambioContenidoProperty);

    public static void SetCambioContenido(DependencyObject elemento, bool valor) => elemento.SetValue(CambioContenidoProperty, valor);

    /// <summary>Desvanecido de entrada con desplazamiento; no hace nada si las animaciones están desactivadas.</summary>
    public static void Aparecer(UIElement elemento)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        if (!Activas)
        {
            return;
        }

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        elemento.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Duracion) { EasingFunction = suave });
        if (elemento.RenderTransform is not TranslateTransform desplazamiento)
        {
            if (elemento.RenderTransform is not null && elemento.RenderTransform != Transform.Identity)
            {
                return;
            }

            desplazamiento = new TranslateTransform();
            elemento.RenderTransform = desplazamiento;
        }

        desplazamiento.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Desplazamiento, 0, Duracion) { EasingFunction = suave });
    }

    private static void AlCambiarEntrada(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement elemento)
        {
            return;
        }

        elemento.Loaded -= AlCargar;
        elemento.IsVisibleChanged -= AlCambiarVisible;
        if (e.NewValue is true)
        {
            elemento.Loaded += AlCargar;
            elemento.IsVisibleChanged += AlCambiarVisible;
        }
    }

    private static void AlCargar(object sender, RoutedEventArgs e) => Aparecer((UIElement)sender);

    private static void AlCambiarVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Aparecer((UIElement)sender);
        }
    }

    private static readonly DependencyPropertyDescriptor Contenido =
        DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));

    private static void AlCambiarCambioContenido(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl control)
        {
            return;
        }

        Contenido.RemoveValueChanged(control, AlCambiarElContenido);
        if (e.NewValue is true)
        {
            Contenido.AddValueChanged(control, AlCambiarElContenido);
        }
    }

    private static void AlCambiarElContenido(object? sender, EventArgs e)
    {
        if (sender is ContentControl { Content: not null } control)
        {
            Aparecer(control);
        }
    }
}
