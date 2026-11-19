using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
}
