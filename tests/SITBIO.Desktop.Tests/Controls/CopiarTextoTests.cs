using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sitbio.Desktop.Controls;

namespace Sitbio.Desktop.Tests.Controls;

/// <summary>I-06: textos clave seleccionables y copia de celdas y filas en las tablas.</summary>
[Collection(nameof(PortapapelesCompartido))]
public class CopiarTextoTests
{
    private sealed record Fila(string Placa, string Equipo, string Estado);

    private static List<string> Copiado(Action accion)
    {
        var copiado = new List<string>();
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            var anterior = Portapapeles.Escribir;
            Portapapeles.Escribir = copiado.Add;
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
                Portapapeles.Escribir = anterior;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        Assert.Null(error);
        return copiado;
    }

    [Fact]
    public void TextoCopiable_EsDeSoloLectura_YCopiaTodoOLaSeleccion()
    {
        var copiado = Copiado(() =>
        {
            var texto = new TextoCopiable { Text = "INVIMA 2021DM-0001234" };
            Assert.True(texto.IsReadOnly);
            Assert.False(texto.IsTabStop);
            Assert.Equal("Copiar", ((MenuItem)texto.ContextMenu!.Items[0]).Header);

            texto.Copiar();
            texto.Select(7, 6);
            texto.Copiar();
        });

        Assert.Equal(["INVIMA 2021DM-0001234", "2021DM"], copiado);
    }

    [Fact]
    public void Tabla_CopiaLaCeldaYLaFilaConTabulaciones()
    {
        var copiado = Copiado(() =>
        {
            var tabla = Tabla();
            tabla.CurrentCell = new DataGridCellInfo(tabla.Items[1], tabla.Columns[1]);

            TablaCopiable.CopiarCelda(tabla);
            TablaCopiable.CopiarFila(tabla);

            Assert.Equal(2, tabla.ContextMenu!.Items.Count);
        });

        Assert.Equal(["Monitor de signos vitales · UCI", "HDS-0002\tMonitor de signos vitales · UCI\tFuera de servicio"], copiado);
    }

    [Fact]
    public void Tabla_SinFila_NoCopiaNada()
    {
        var copiado = Copiado(() => TablaCopiable.CopiarFila(Tabla(abrir: false)));

        Assert.Empty(copiado);
    }

    [Fact]
    public void LaTablaAbierta_ActivaLaCopia() =>
        Assert.Matches(new Regex(@"<Style x:Key=""OpenGrid"" TargetType=""DataGrid"">\s*<!--[^>]*-->\s*<Setter Property=""controls:TablaCopiable.Activa"" Value=""True"" />"),
            File.ReadAllText(Path.Combine(Raiz(), "src", "SITBIO.Desktop", "Themes", "Controls.xaml")));

    private static DataGrid Tabla(bool abrir = true)
    {
        var tabla = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { new Fila("HDS-0001", "Bomba", "Operativo"), new Fila("HDS-0002", "Monitor de signos vitales", "Fuera de servicio") } };
        TablaCopiable.SetActiva(tabla, true);
        tabla.Columns.Add(new DataGridTextColumn { Header = "Placa", Binding = new Binding(nameof(Fila.Placa)) });

        // Columna de plantilla con dos textos, como las de las vistas.
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        var nombre = new FrameworkElementFactory(typeof(TextBlock));
        nombre.SetBinding(TextBlock.TextProperty, new Binding(nameof(Fila.Equipo)));
        var ubicacion = new FrameworkElementFactory(typeof(TextBlock));
        ubicacion.SetValue(TextBlock.TextProperty, "· UCI");
        panel.AppendChild(nombre);
        panel.AppendChild(ubicacion);
        tabla.Columns.Add(new DataGridTemplateColumn { Header = "Equipo", CellTemplate = new DataTemplate { VisualTree = panel } });
        tabla.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(Fila.Estado)) });

        if (abrir)
        {
            var ventana = new Window { Content = tabla, Width = 800, Height = 300, Left = -5000, Top = -5000, ShowInTaskbar = false };
            ventana.Show();
            tabla.UpdateLayout();
        }

        return tabla;
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

/// <summary>Las pruebas que reemplazan el portapapeles no corren en paralelo.</summary>
[CollectionDefinition(nameof(PortapapelesCompartido), DisableParallelization = true)]
public class PortapapelesCompartido;
