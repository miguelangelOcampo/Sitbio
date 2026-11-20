using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sitbio.Desktop.Views;

namespace Sitbio.Desktop.Tests.Views;

/// <summary>
/// H-02: transición al cambiar de pantalla, aparición de diálogos y menús y esqueleto de carga, sin animar cuando
/// Windows tiene «Reducir animaciones».
/// </summary>
[Collection(nameof(AnimacionTests))]
[CollectionDefinition(nameof(AnimacionTests), DisableParallelization = true)]
public class AnimacionTests
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
            finally
            {
                Animacion.Forzar = null;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        Assert.Null(error);
    }

    [Fact]
    public void ConAnimacionesDesactivadas_ElElementoApareceSinAnimar() => EnSta(() =>
    {
        Animacion.Forzar = false;
        var elemento = new Border();

        Animacion.Aparecer(elemento);

        Assert.False(elemento.HasAnimatedProperties);
        Assert.Equal(Transform.Identity, elemento.RenderTransform);
    });

    [Fact]
    public void ConAnimaciones_AnimaLaOpacidadYElDesplazamiento() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var elemento = new Border();

        Animacion.Aparecer(elemento);

        Assert.True(elemento.HasAnimatedProperties);
        Assert.True(Assert.IsType<TranslateTransform>(elemento.RenderTransform).HasAnimatedProperties);
    });

    [Fact]
    public void CambioDeContenido_AnimaSoloCuandoHayPantallaNueva() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var control = new ContentControl();
        Animacion.SetCambioContenido(control, true);

        control.Content = null;
        Assert.False(control.HasAnimatedProperties);

        control.Content = "Pantalla";
        Assert.True(control.HasAnimatedProperties);
    });

    [Fact]
    public void UnaTransformacionPropia_NoSeReemplaza() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var giro = new RotateTransform(5);
        var elemento = new Border { RenderTransform = giro };

        Animacion.Aparecer(elemento);

        Assert.Same(giro, elemento.RenderTransform);
    });

    [Fact]
    public void Esqueleto_TieneLasFilasPedidas_YElTextoComoNombreAccesible() => EnSta(() =>
    {
        var esqueleto = new Esqueleto { Filas = 3, Texto = "Consultando las órdenes…" };

        var pila = Assert.IsType<StackPanel>(esqueleto.Content);
        Assert.Equal(3, pila.Children.Count);
        Assert.All(pila.Children.Cast<Border>(), f => Assert.Equal(Esqueleto.Columnas.Length, ((Grid)f.Child).Children.Count));
        Assert.Equal("Consultando las órdenes…", AutomationProperties.GetName(esqueleto));
        Assert.False(esqueleto.Focusable);
    });

    // ---------- I-14: KPI escalonados y gráficos que crecen ----------

    [Fact]
    public void ConRetraso_QuedaInvisibleHastaSuTurno() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var elemento = new Border();

        Animacion.Aparecer(elemento, Animacion.Paso * 2);
        var tramo = Assert.IsType<DoubleAnimationUsingKeyFrames>(Animacion.Tramo(0, 1, Animacion.Paso * 2, Animacion.Duracion, new CubicEase()));

        Assert.True(elemento.HasAnimatedProperties);
        Assert.Equal([0.0, 0.0, 1.0], tramo.KeyFrames.Cast<DoubleKeyFrame>().Select(k => k.Value));
        Assert.Equal(Animacion.Paso * 2, tramo.KeyFrames[1].KeyTime.TimeSpan);
    });

    [Fact]
    public void Crecer_EscalaDesdeLaBase_YSinAnimacionesNoHaceNada() => EnSta(() =>
    {
        Animacion.Forzar = false;
        var quieto = new Border();
        Animacion.Crecer(quieto);
        Assert.False(quieto.HasAnimatedProperties);
        Assert.Equal(Transform.Identity, quieto.RenderTransform);

        Animacion.Forzar = true;
        var elemento = new Border();
        Animacion.Crecer(elemento);
        Assert.True(Assert.IsType<ScaleTransform>(elemento.RenderTransform).HasAnimatedProperties);
        Assert.Equal(new Point(0.5, 1), elemento.RenderTransformOrigin);
    });

    [Fact]
    public void Crecer_RespetaUnaTransformacionPropia() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var giro = new RotateTransform(10);
        var elemento = new Border { RenderTransform = giro };

        Animacion.Crecer(elemento);

        Assert.Same(giro, elemento.RenderTransform);
    });

    [Fact]
    public void Escalonado_CadaElementoEntraDespuesDelAnterior() => EnSta(() =>
    {
        Animacion.Forzar = true;
        var lista = new ItemsControl { ItemsSource = new[] { "A", "B", "C" } };
        var ventana = new Window { Content = lista, Width = 300, Height = 200, Left = -5000, Top = -5000, ShowInTaskbar = false };
        ventana.Show();
        lista.UpdateLayout();

        Animacion.AparecerEscalonado(lista);

        var contenedores = Enumerable.Range(0, 3).Select(i => (UIElement)lista.ItemContainerGenerator.ContainerFromIndex(i)).ToList();
        Assert.All(contenedores, c => Assert.True(c.HasAnimatedProperties));
        ventana.Close();
    });

    [Fact]
    public void LosKpiYLosGraficosUsanLaEntrada()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "SITBIO.sln")))
        {
            raiz = raiz.Parent;
        }

        var vistas = Path.Combine(raiz!.FullName, "src", "SITBIO.Desktop", "Views");
        var panel = File.ReadAllText(Path.Combine(vistas, "PanelView.xaml"));
        var tema = File.ReadAllText(Path.Combine(raiz.FullName, "src", "SITBIO.Desktop", "Themes", "Controls.xaml"));

        Assert.Contains(@"ItemsSource=""{Binding Indicadores}"" ItemTemplate=""{StaticResource PlantillaIndicador}"" Focusable=""False"" views:Animacion.Escalonar=""True""", panel);
        Assert.Contains(@"<Setter Property=""views:Animacion.Crecer"" Value=""True"" />", tema);
        foreach (var archivo in Directory.GetFiles(vistas, "*.xaml"))
        {
            foreach (var grafico in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(archivo), @"<controls:Grafico\b[^>]*>").Select(m => m.Value))
            {
                Assert.True(grafico.Contains("MiniGrafico") || grafico.Contains(@"views:Animacion.Crecer=""True"""), $"{Path.GetFileName(archivo)}: {grafico}");
            }
        }
    }
}
