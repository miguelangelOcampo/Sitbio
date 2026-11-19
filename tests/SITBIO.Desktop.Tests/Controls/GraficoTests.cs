using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Controls;

namespace Sitbio.Desktop.Tests.Controls;

/// <summary>I-01: componente de gráficos con etiqueta al pasar el mouse (línea, área, barras y dispersión).</summary>
public class GraficoTests
{
    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                accion();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        Assert.Null(error);
    }

    private static Grafico Lineas(TipoGrafico tipo = TipoGrafico.Linea) => new()
    {
        Tipo = tipo,
        Etiquetas = ["jun", "jul", "ago"],
        Series =
        [
            new SerieGrafico("Preventivos", "Brush.Primary", [3, 5, 2]),
            new SerieGrafico("Correctivos", "Brush.Jade", [1, 0, 4], Discontinua: true),
        ],
    };

    private static Grafico Dispersion(ICommand? comando = null) => new()
    {
        Tipo = TipoGrafico.Dispersion,
        TituloEjeX = "Fallas por año",
        TituloEjeY = "Costo",
        Series = [new SerieGrafico("Estables", "Brush.Primary", []), new SerieGrafico("Alto costo", "Brush.Danger", [])],
        Puntos =
        [
            new PuntoGrafico(1, 200, 0, "PL-00123", "Bomba de infusión"),
            new PuntoGrafico(6, 900, 1, "PL-00087", "Monitor multiparámetro"),
        ],
        ComandoPunto = comando,
    };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(6.6, 2)]
    [InlineData(18, 5)]
    [InlineData(33, 10)]
    [InlineData(950, 250)]
    public void Paso_EsRedondo_YCubreElMaximoEnCuatroDivisiones(double maximo, double esperado)
    {
        Assert.Equal(esperado, Grafico.Paso(maximo));
        Assert.True(Grafico.Paso(maximo) * 4 >= maximo);
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(49, false, 0)]
    [InlineData(51, false, 1)]
    [InlineData(200, false, 2)]
    [InlineData(500, false, 2)]
    [InlineData(-30, false, 0)]
    [InlineData(66, true, 0)]
    [InlineData(67, true, 1)]
    [InlineData(199, true, 2)]
    public void IndiceMasCercano_EnLineasYEnBarras(double x, bool barras, int esperado) =>
        Assert.Equal(esperado, Grafico.IndiceMasCercano(x, 0, 200, 3, barras));

    [Fact]
    public void LaEtiqueta_MuestraCadaSerieConSuValor_YElPeriodo() => EnSta(() =>
    {
        var grafico = Lineas();

        grafico.Resaltar(1);

        Assert.Equal("Preventivos: 5 · Correctivos: 0\njul", grafico.TextoEtiqueta);
    });

    [Fact]
    public void LaEtiquetaDeDispersion_MuestraElPuntoLosEjesYElGrupo() => EnSta(() =>
    {
        var grafico = Dispersion();
        grafico.FormatoValor = "N0";

        grafico.Resaltar(1);

        Assert.Equal("PL-00087\nMonitor multiparámetro\nFallas por año: 6 · Costo: 900\nAlto costo", grafico.TextoEtiqueta);
    });

    [Fact]
    public void ConLasFlechas_SeRecorrenLosPeriodos_YEscapeQuitaLaEtiqueta() => EnSta(() =>
    {
        var grafico = Lineas();

        Tecla(grafico, Key.Right);
        Assert.Equal(0, grafico.Resaltado);
        Tecla(grafico, Key.Right);
        Tecla(grafico, Key.Right);
        Tecla(grafico, Key.Right);
        Assert.Equal(2, grafico.Resaltado);
        Tecla(grafico, Key.Left);
        Assert.Equal(1, grafico.Resaltado);
        Tecla(grafico, Key.Escape);
        Assert.Null(grafico.Resaltado);
    });

    [Fact]
    public void EnterSobreUnPunto_EjecutaElComandoConEsePunto() => EnSta(() =>
    {
        PuntoGrafico? elegido = null;
        var grafico = Dispersion(new RelayCommand<PuntoGrafico>(p => elegido = p));

        grafico.Resaltar(0);
        Tecla(grafico, Key.Enter);

        Assert.Equal("PL-00123", elegido?.Titulo);
    });

    [Fact]
    public void CambiarLosDatos_QuitaLaEtiqueta_YUnIndiceFueraDeRangoSeIgnora() => EnSta(() =>
    {
        var grafico = Lineas();
        grafico.Resaltar(2);

        grafico.Etiquetas = ["a", "b", "c"];
        Assert.Null(grafico.Resaltado);

        grafico.Resaltar(7);
        Assert.Null(grafico.Resaltado);
    });

    [Theory]
    [InlineData(TipoGrafico.Linea)]
    [InlineData(TipoGrafico.Area)]
    [InlineData(TipoGrafico.Barras)]
    [InlineData(TipoGrafico.Dispersion)]
    public void SeDibuja_ConYSinEtiqueta(TipoGrafico tipo) => EnSta(() =>
    {
        var grafico = tipo == TipoGrafico.Dispersion ? Dispersion() : Lineas(tipo);
        Dibujar(grafico);

        grafico.Resaltar(1);
        Dibujar(grafico);
    });

    [Fact]
    public void SinDatos_SeDibujaVacio() => EnSta(() => Dibujar(new Grafico()));

    private static void Dibujar(Grafico grafico)
    {
        grafico.Measure(new Size(480, 220));
        grafico.Arrange(new Rect(0, 0, 480, 220));
        grafico.UpdateLayout();
        var imagen = new RenderTargetBitmap(480, 220, 96, 96, PixelFormats.Pbgra32);
        imagen.Render(grafico);
    }

    private static void Tecla(Grafico grafico, Key tecla)
    {
        var fuente = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("prueba"));
        try
        {
            grafico.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, fuente, 0, tecla) { RoutedEvent = UIElement.KeyDownEvent });
        }
        finally
        {
            fuente.Dispose();
        }
    }
}
