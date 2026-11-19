using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sitbio.Desktop.Views;

namespace Sitbio.Desktop.Tests.Views;

/// <summary>G-04: la rueda del mouse sobre una tabla sin desplazamiento propio baja la página.</summary>
public class RuedaHaciaPadreTests
{
    [Theory]
    [InlineData(0, 0, -120, true)]      // sin desplazamiento propio
    [InlineData(300, 0, 120, true)]     // arriba del todo y sube
    [InlineData(300, 0, -120, false)]   // arriba del todo y baja: la tabla se desplaza
    [InlineData(300, 150, -120, false)] // en medio
    [InlineData(300, 300, -120, true)]  // abajo del todo y baja
    [InlineData(300, 300, 120, false)]  // abajo del todo y sube
    public void DebeReenviar_SoloSiLaTablaNoPuedeDesplazarse(double desplazable, double posicion, int delta, bool esperado) =>
        Assert.Equal(esperado, RuedaHaciaPadre.DebeReenviar(desplazable, posicion, delta));

    [Fact]
    public void TablaSinDesplazamiento_PasaLaRuedaAlContenedor()
    {
        EnSta(() =>
        {
            var tabla = new DataGrid();
            RuedaHaciaPadre.SetActiva(tabla, true);
            var pagina = new Border { Child = tabla };
            var recibidas = 0;
            pagina.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((_, _) => recibidas++));

            var rueda = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            tabla.RaiseEvent(rueda);

            Assert.True(rueda.Handled);
            Assert.Equal(1, recibidas);
        });
    }

    [Fact]
    public void SinElComportamiento_LaTablaNoReenvia()
    {
        EnSta(() =>
        {
            var tabla = new DataGrid();
            RuedaHaciaPadre.SetActiva(tabla, true);
            RuedaHaciaPadre.SetActiva(tabla, false);
            var pagina = new Border { Child = tabla };
            var recibidas = 0;
            pagina.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((_, _) => recibidas++));

            var rueda = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            tabla.RaiseEvent(rueda);

            Assert.False(rueda.Handled);
            Assert.Equal(0, recibidas);
        });
    }

    private static void EnSta(Action prueba)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                prueba();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null)
        {
            throw error;
        }
    }
}
