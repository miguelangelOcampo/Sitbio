using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Controls;

namespace Sitbio.Desktop.Tests.Controls;

/// <summary>I-09: las filas que abren un detalle se ven clicables y abren con un solo clic o Enter.</summary>
public class FilaClicableTests
{
    private sealed record Fila(string Placa);

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

    private static (DataGrid Tabla, Window Ventana) Tabla(List<Fila> abiertas)
    {
        var tabla = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = new[] { new Fila("HDS-0001"), new Fila("HDS-0002") } };
        tabla.Columns.Add(new DataGridTextColumn { Header = "Placa", Binding = new Binding(nameof(Fila.Placa)) });
        var boton = new FrameworkElementFactory(typeof(Button));
        boton.SetValue(ContentControl.ContentProperty, "Acción");
        tabla.Columns.Add(new DataGridTemplateColumn { Header = "", CellTemplate = new DataTemplate { VisualTree = boton } });
        FilaClicable.SetComando(tabla, new RelayCommand<Fila>(f => abiertas.Add(f!)));
        var ventana = new Window { Content = tabla, Width = 600, Height = 300, Left = -5000, Top = -5000, ShowInTaskbar = false };
        ventana.Show();
        tabla.UpdateLayout();
        return (tabla, ventana);
    }

    private static T Hijo<T>(DependencyObject d)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var hijo = VisualTreeHelper.GetChild(d, i);
            if (hijo is T encontrado)
            {
                return encontrado;
            }

            if (Hijo<T>(hijo) is { } mas)
            {
                return mas;
            }
        }

        return null!;
    }

    private static void Soltar(UIElement donde) =>
        donde.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });

    [Fact]
    public void UnClicEnLaFila_AbreElDetalle_YLosBotonesDeLaFilaNo() => EnSta(() =>
    {
        var abiertas = new List<Fila>();
        var (tabla, ventana) = Tabla(abiertas);
        var segunda = (DataGridRow)tabla.ItemContainerGenerator.ContainerFromIndex(1);

        Soltar(Hijo<TextBlock>(Hijo<DataGridCell>(segunda)));
        Soltar(Hijo<Button>((DataGridRow)tabla.ItemContainerGenerator.ContainerFromIndex(0)));

        Assert.Equal([new Fila("HDS-0002")], abiertas);
        ventana.Close();
    });

    [Fact]
    public void Enter_AbreLaFilaSeleccionada() => EnSta(() =>
    {
        var abiertas = new List<Fila>();
        var (tabla, ventana) = Tabla(abiertas);
        tabla.SelectedIndex = 0;

        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tabla), 0, Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent };
        tabla.RaiseEvent(enter);

        Assert.True(enter.Handled);
        Assert.Equal([new Fila("HDS-0001")], abiertas);
        ventana.Close();
    });

    [Fact]
    public void AgregaElChevron_YMarcaLasFilasComoClicables() => EnSta(() =>
    {
        var (tabla, ventana) = Tabla([]);

        Assert.Equal(FilaClicable.Chevron, tabla.Columns[^1].Header);
        Assert.Equal(3, tabla.Columns.Count);
        Assert.True(FilaClicable.GetActiva((DataGridRow)tabla.ItemContainerGenerator.ContainerFromIndex(0)));

        // Volver a poner el comando no duplica el chevron.
        FilaClicable.SetComando(tabla, new RelayCommand(() => { }));
        Assert.Equal(3, tabla.Columns.Count);
        ventana.Close();
    });

    [Fact]
    public void SinFilaOComandoQueNoPuede_NoAbre() => EnSta(() =>
    {
        var tabla = new DataGrid();
        FilaClicable.SetComando(tabla, new RelayCommand<object>(_ => { }, _ => false));

        Assert.False(FilaClicable.Abrir(tabla, null));
        Assert.False(FilaClicable.Abrir(tabla, new Fila("HDS-0001")));
    });

    [Theory]
    [InlineData("EquiposView", "AbrirCommand", "Haz clic en un equipo para ver su hoja de vida.")]
    [InlineData("BandejaView", "AbrirAsignacionCommand", "Haz clic en un tiquete para asignar su orden de trabajo.")]
    [InlineData("SeguimientoView", "AbrirCommand", "Haz clic en un tiquete para ver su seguimiento.")]
    public void LasTablasQueAbrenDetalle_UsanUnClic_YDicenComo(string vista, string comando, string ayuda)
    {
        var texto = File.ReadAllText(Path.Combine(Raiz(), "src", "SITBIO.Desktop", "Views", vista + ".xaml"));

        Assert.Contains($@"controls:FilaClicable.Comando=""{{Binding {comando}}}""", texto);
        Assert.Contains(ayuda, texto);
        Assert.DoesNotMatch(new Regex("MouseDoubleClick"), texto);
    }

    [Fact]
    public void ElEstiloDeFila_PoneManoYFondoAlPasarElMouse()
    {
        var tema = File.ReadAllText(Path.Combine(Raiz(), "src", "SITBIO.Desktop", "Themes", "Controls.xaml"));

        Assert.Matches(new Regex(@"<Trigger Property=""controls:FilaClicable.Activa"" Value=""True"">\s*<Setter Property=""Cursor"" Value=""Hand"" />"), tema);
        Assert.Matches(new Regex(@"<Condition Property=""IsMouseOver"" Value=""True"" />\s*</MultiTrigger.Conditions>\s*<Setter Property=""Background"" Value=""\{DynamicResource Brush.Surface2\}"" />"), tema);
    }

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SITBIO.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
