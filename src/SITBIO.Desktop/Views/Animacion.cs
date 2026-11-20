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
    public static void Aparecer(UIElement elemento) => Aparecer(elemento, TimeSpan.Zero);

    /// <summary>Como <see cref="Aparecer(UIElement)"/>, pero empieza tras <paramref name="retraso"/> (invisible mientras espera).</summary>
    public static void Aparecer(UIElement elemento, TimeSpan retraso)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        if (!Activas)
        {
            return;
        }

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        elemento.BeginAnimation(UIElement.OpacityProperty, Tramo(0, 1, retraso, Duracion, suave));
        if (elemento.RenderTransform is not TranslateTransform desplazamiento)
        {
            if (elemento.RenderTransform is not null && elemento.RenderTransform != Transform.Identity)
            {
                return;
            }

            desplazamiento = new TranslateTransform();
            elemento.RenderTransform = desplazamiento;
        }

        desplazamiento.BeginAnimation(TranslateTransform.YProperty, Tramo(Desplazamiento, 0, retraso, Duracion, suave));
    }

    /// <summary>Animación de <paramref name="desde"/> a <paramref name="hasta"/> que se queda en el valor inicial durante el retraso.</summary>
    public static AnimationTimeline Tramo(double desde, double hasta, TimeSpan retraso, Duration duracion, IEasingFunction suave)
    {
        if (retraso <= TimeSpan.Zero)
        {
            return new DoubleAnimation(desde, hasta, duracion) { EasingFunction = suave };
        }

        var tramo = new DoubleAnimationUsingKeyFrames();
        tramo.KeyFrames.Add(new DiscreteDoubleKeyFrame(desde, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        tramo.KeyFrames.Add(new DiscreteDoubleKeyFrame(desde, KeyTime.FromTimeSpan(retraso)));
        tramo.KeyFrames.Add(new EasingDoubleKeyFrame(hasta, KeyTime.FromTimeSpan(retraso + duracion.TimeSpan), suave));
        return tramo;
    }

    // ---------- I-14: entrada escalonada de los indicadores y crecimiento de los gráficos ----------

    /// <summary>Distancia entre la entrada de un elemento de la lista y el siguiente.</summary>
    public static readonly TimeSpan Paso = TimeSpan.FromMilliseconds(60);

    public static readonly Duration DuracionCrecer = new(TimeSpan.FromMilliseconds(420));

    /// <summary>Los elementos de un ItemsControl aparecen uno tras otro al cargarse o al cambiar la lista.</summary>
    public static readonly DependencyProperty EscalonarProperty = DependencyProperty.RegisterAttached(
        "Escalonar", typeof(bool), typeof(Animacion), new PropertyMetadata(false, AlCambiarEscalonar));

    public static bool GetEscalonar(DependencyObject elemento) => (bool)elemento.GetValue(EscalonarProperty);

    public static void SetEscalonar(DependencyObject elemento, bool valor) => elemento.SetValue(EscalonarProperty, valor);

    /// <summary>El gráfico crece desde su base y se desvanece al cargarse o volverse visible.</summary>
    public static readonly DependencyProperty CrecerProperty = DependencyProperty.RegisterAttached(
        "Crecer", typeof(bool), typeof(Animacion), new PropertyMetadata(false, AlCambiarCrecer));

    public static bool GetCrecer(DependencyObject elemento) => (bool)elemento.GetValue(CrecerProperty);

    public static void SetCrecer(DependencyObject elemento, bool valor) => elemento.SetValue(CrecerProperty, valor);

    private static readonly DependencyProperty EscalonadoPendienteProperty = DependencyProperty.RegisterAttached(
        "EscalonadoPendiente", typeof(bool), typeof(Animacion), new PropertyMetadata(false));

    /// <summary>Hace aparecer cada contenedor de la lista con <see cref="Paso"/> de diferencia.</summary>
    public static void AparecerEscalonado(ItemsControl lista)
    {
        ArgumentNullException.ThrowIfNull(lista);
        for (var i = 0; i < lista.Items.Count; i++)
        {
            if (lista.ItemContainerGenerator.ContainerFromIndex(i) is UIElement contenedor)
            {
                Aparecer(contenedor, Paso * i);
            }
        }
    }

    /// <summary>Crecimiento vertical desde la base con desvanecido; respeta una transformación propia.</summary>
    public static void Crecer(UIElement elemento)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        if (!Activas)
        {
            return;
        }

        if (elemento.RenderTransform is not ScaleTransform escala)
        {
            if (elemento.RenderTransform is not null && elemento.RenderTransform != Transform.Identity)
            {
                return;
            }

            escala = new ScaleTransform();
            elemento.RenderTransform = escala;
            elemento.RenderTransformOrigin = new Point(0.5, 1);
        }

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1, DuracionCrecer) { EasingFunction = suave });
        elemento.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, DuracionCrecer) { EasingFunction = suave });
    }

    private static void AlCambiarEscalonar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl lista)
        {
            return;
        }

        lista.Loaded -= AlCargarLista;
        if (e.NewValue is true)
        {
            lista.Loaded += AlCargarLista;

            // Se pone una vez desde XAML; los cambios de la lista solo animan si sigue activo.
            lista.ItemContainerGenerator.ItemsChanged += (_, _) =>
            {
                if (GetEscalonar(lista))
                {
                    Programar(lista);
                }
            };
        }
    }

    private static void AlCargarLista(object sender, RoutedEventArgs e) => Programar((ItemsControl)sender);

    /// <summary>Agrupa los cambios de la lista (Clear y varios Add) en una sola entrada escalonada.</summary>
    private static void Programar(ItemsControl lista)
    {
        if (!Activas || (bool)lista.GetValue(EscalonadoPendienteProperty))
        {
            return;
        }

        lista.SetValue(EscalonadoPendienteProperty, true);
        lista.Dispatcher.BeginInvoke(
            () =>
            {
                lista.SetValue(EscalonadoPendienteProperty, false);
                AparecerEscalonado(lista);
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void AlCambiarCrecer(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement elemento)
        {
            return;
        }

        elemento.Loaded -= AlCargarCrecer;
        elemento.IsVisibleChanged -= AlVolverVisibleCrecer;
        if (e.NewValue is true)
        {
            elemento.Loaded += AlCargarCrecer;
            elemento.IsVisibleChanged += AlVolverVisibleCrecer;
        }
    }

    private static void AlCargarCrecer(object sender, RoutedEventArgs e) => Crecer((UIElement)sender);

    private static void AlVolverVisibleCrecer(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Crecer((UIElement)sender);
        }
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
