using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sitbio.Desktop.Controls;

/// <summary>Gráfico capturado de la pantalla: su título y la imagen PNG.</summary>
public sealed record GraficoCapturado(string Titulo, byte[] Png);

/// <summary>
/// Captura los gráficos de una pantalla para exportarlos a PDF (J-03): cada <see cref="Grafico"/> visible que tenga
/// <see cref="Grafico.Titulo"/>, en el orden de la pantalla, como PNG a 2x (192 ppp) sobre el fondo de la tarjeta.
/// Los mini gráficos de los indicadores no tienen título y no se exportan. Un bloque con <see cref="TituloProperty"/>
/// (por ejemplo la dona del parque con su número central y su leyenda) se captura entero en lugar de sus gráficos.
/// </summary>
public static class CapturaGraficos
{
    /// <summary>Lo mismo que acepta la API por PDF.</summary>
    public const int Maximo = 6;

    public const double Escala = 2;

    /// <summary>Título con el que se exporta el bloque completo; los gráficos de adentro no se capturan aparte.</summary>
    public static readonly DependencyProperty TituloProperty = DependencyProperty.RegisterAttached(
        "Titulo", typeof(string), typeof(CapturaGraficos), new PropertyMetadata(string.Empty));

    public static string GetTitulo(DependencyObject d) => (string)d.GetValue(TituloProperty);

    public static void SetTitulo(DependencyObject d, string valor) => d.SetValue(TituloProperty, valor);

    public static IReadOnlyList<GraficoCapturado> Capturar(DependencyObject raiz)
    {
        var capturas = new List<GraficoCapturado>();
        var bloques = new List<FrameworkElement>();
        foreach (var elemento in Descendientes(raiz).OfType<FrameworkElement>())
        {
            if (capturas.Count == Maximo)
            {
                break;
            }

            var titulo = GetTitulo(elemento);
            if (string.IsNullOrWhiteSpace(titulo))
            {
                if (elemento is not Grafico grafico || bloques.Any(b => b.IsAncestorOf(grafico)))
                {
                    continue;
                }

                titulo = grafico.Titulo;
            }
            else
            {
                bloques.Add(elemento);
            }

            if (elemento.IsVisible && !string.IsNullOrWhiteSpace(titulo) && elemento.ActualWidth >= 1 && elemento.ActualHeight >= 1)
            {
                capturas.Add(new GraficoCapturado(titulo, Png(elemento)));
            }
        }

        return capturas;
    }

    /// <summary>El elemento como PNG a <see cref="Escala"/>x, sobre el pincel «Brush.Surface» del tema.</summary>
    public static byte[] Png(FrameworkElement elemento)
    {
        var ancho = elemento.ActualWidth;
        var alto = elemento.ActualHeight;
        var mapa = new RenderTargetBitmap(
            (int)Math.Ceiling(ancho * Escala), (int)Math.Ceiling(alto * Escala), 96 * Escala, 96 * Escala, PixelFormats.Pbgra32);
        var dibujo = new DrawingVisual();
        using (var dc = dibujo.RenderOpen())
        {
            var area = new Rect(0, 0, ancho, alto);
            dc.DrawRectangle(elemento.TryFindResource("Brush.Surface") as Brush ?? Brushes.White, null, area);
            dc.DrawRectangle(new VisualBrush(elemento), null, area);
        }

        mapa.Render(dibujo);
        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(mapa));
        using var salida = new MemoryStream();
        codificador.Save(salida);
        return salida.ToArray();
    }

    private static IEnumerable<DependencyObject> Descendientes(DependencyObject padre)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(padre); i++)
        {
            var hijo = VisualTreeHelper.GetChild(padre, i);
            yield return hijo;
            foreach (var nieto in Descendientes(hijo))
            {
                yield return nieto;
            }
        }
    }
}
