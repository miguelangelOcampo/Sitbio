using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Controls;

namespace Sitbio.Desktop.Tests.Controls;

/// <summary>I-01: componente de gráficos con etiqueta al pasar el mouse (línea, área, barras, dispersión y arco; I-02 mini gráficos).</summary>
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

    // J-02: la etiqueta va en la capa superior y busca dónde cabe dentro de la ventana.
    private static readonly Rect Ventana = new(0, 0, 400, 300);
    private static readonly Size Globo = new(120, 40);

    [Theory]
    [InlineData(100, 100, 110, 50)] // cabe a la derecha y arriba
    [InlineData(350, 100, 220, 50)] // no cabe a la derecha: a la izquierda
    [InlineData(100, 30, 110, 40)] // no cabe arriba: abajo
    [InlineData(395, 10, 265, 20)] // esquina superior derecha: a la izquierda y abajo
    [InlineData(5, 295, 15, 245)] // esquina inferior izquierda: a la derecha y arriba
    public void LaEtiqueta_SeUbicaDondeCabe(double x, double y, double esperadoX, double esperadoY) =>
        Assert.Equal(new Point(esperadoX, esperadoY), Grafico.UbicarEtiqueta(new Point(x, y), Globo, Ventana));

    [Fact]
    public void UnaEtiquetaMasGrandeQueLaVentana_SePegaAlBordeSuperiorIzquierdo() =>
        Assert.Equal(new Point(0, 0), Grafico.UbicarEtiqueta(new Point(50, 50), new Size(500, 400), Ventana));

    [Fact]
    public void EnUnaVentana_LaEtiquetaSeAbreEnLaCapaSuperior_YSeCierraAlSalir() => EnSta(() =>
    {
        var grafico = Dispersion();
        var ventana = new Window { Width = 420, Height = 320, Content = grafico, ShowInTaskbar = false, ShowActivated = false, Left = -2000, Top = -2000 };
        try
        {
            ventana.Show();
            ventana.UpdateLayout();
            Assert.False(grafico.EtiquetaAbierta);

            // El punto de más costo está arriba a la derecha: la etiqueta va a su izquierda y abajo, sin salirse.
            grafico.Resaltar(1);
            Assert.True(grafico.EtiquetaAbierta);
            var posicion = grafico.PosicionEtiqueta!.Value;
            Assert.InRange(posicion.X, 0, grafico.ActualWidth);
            Assert.InRange(posicion.Y, 0, grafico.ActualHeight);

            grafico.Resaltar(null);
            Assert.False(grafico.EtiquetaAbierta);

            // Cambiar los datos también la quita.
            grafico.Resaltar(0);
            Assert.True(grafico.EtiquetaAbierta);
            grafico.Puntos = [];
            Assert.False(grafico.EtiquetaAbierta);
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public void FueraDeUnaVentana_NoAbreLaEtiqueta_PeroConservaElTexto() => EnSta(() =>
    {
        var grafico = Lineas();
        Dibujar(grafico);
        grafico.Resaltar(1);
        Assert.NotNull(grafico.TextoEtiqueta);
        Assert.False(grafico.EtiquetaAbierta);
    });

    private static Grafico Arco(bool compacto = false) => new()
    {
        Tipo = TipoGrafico.Arco,
        Compacto = compacto,
        Series =
        [
            new SerieGrafico("Operativo", "Brush.Success", [6]),
            new SerieGrafico("En mantenimiento", "Brush.Warning", [0]),
            new SerieGrafico("Fuera de servicio", "Brush.Danger", [2]),
        ],
    };

    [Fact]
    public void LaEtiquetaDelArco_MuestraElValorYElPorcentaje() => EnSta(() =>
    {
        var grafico = Arco();

        grafico.Resaltar(2);
        Assert.Equal("Fuera de servicio: 2 (25 %)", grafico.TextoEtiqueta);

        grafico.Resaltar(5);
        Assert.Null(grafico.Resaltado);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElArco_SeDibujaConYSinEtiqueta(bool compacto) => EnSta(() =>
    {
        var grafico = Arco(compacto);
        Dibujar(grafico);

        grafico.Resaltar(0);
        Dibujar(grafico);
    });

    [Theory]
    [InlineData(TipoGrafico.Linea)]
    [InlineData(TipoGrafico.Barras)]
    public void ElMiniGrafico_ConservaLaEtiqueta_YSeDibuja(TipoGrafico tipo) => EnSta(() =>
    {
        var grafico = Lineas(tipo);
        grafico.Compacto = true;
        Dibujar(grafico);

        grafico.Resaltar(2);
        Assert.Equal("Preventivos: 2 · Correctivos: 4\nago", grafico.TextoEtiqueta);
        Dibujar(grafico);
    });

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
