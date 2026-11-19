using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Indicador de carga tipo esqueleto para las tablas (H-02, prototipo): filas grises con el ancho aproximado de las
/// columnas que laten mientras se consulta. Con «Reducir animaciones» se ven quietas. <see cref="Texto"/> queda como
/// nombre accesible para el lector de pantalla («Consultando las órdenes…»).
/// </summary>
public sealed class Esqueleto : ContentControl
{
    /// <summary>Ancho relativo de las barras de cada fila: código, equipo, estado, fecha y acción.</summary>
    public static readonly double[] Columnas = [0.9, 2.2, 1.2, 1.1, 0.8];

    public static readonly DependencyProperty FilasProperty = DependencyProperty.Register(
        nameof(Filas), typeof(int), typeof(Esqueleto), new PropertyMetadata(5, (d, _) => ((Esqueleto)d).Construir()));

    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(Esqueleto),
        new PropertyMetadata("Cargando…", (d, e) => AutomationProperties.SetName(d, (string)e.NewValue)));

    private readonly DoubleAnimation _latido = new(1, 0.45, new Duration(TimeSpan.FromMilliseconds(850)))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };

    public Esqueleto()
    {
        Focusable = false;
        IsTabStop = false;
        AutomationProperties.SetName(this, Texto);
        Construir();
        IsVisibleChanged += (_, e) => Latir(e.NewValue is true);
    }

    public int Filas
    {
        get => (int)GetValue(FilasProperty);
        set => SetValue(FilasProperty, value);
    }

    public string Texto
    {
        get => (string)GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    /// <summary>El latido corre solo mientras se ve y si Windows permite animaciones.</summary>
    public bool Late { get; private set; }

    private void Construir()
    {
        var pila = new StackPanel();
        for (var i = 0; i < Math.Max(Filas, 1); i++)
        {
            var fila = new Grid { Height = 52 };
            for (var c = 0; c < Columnas.Length; c++)
            {
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Columnas[c], GridUnitType.Star) });
                var barra = new Border
                {
                    Height = 12,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(10, 0, 18 + (((i + c) % 3) * 14), 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                barra.SetResourceReference(Border.BackgroundProperty, "Brush.Surface3");
                Grid.SetColumn(barra, c);
                fila.Children.Add(barra);
            }

            var linea = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = fila };
            linea.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            pila.Children.Add(linea);
        }

        Content = pila;
    }

    private void Latir(bool visible)
    {
        Late = visible && Animacion.Activas;
        BeginAnimation(OpacityProperty, Late ? _latido : null);
    }
}
