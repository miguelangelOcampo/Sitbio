using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Sitbio.Desktop.Controls;

/// <summary>
/// Tablas cuyas filas abren un detalle (I-09): un solo clic o Enter ejecuta <see cref="ComandoProperty"/> con la fila;
/// el estilo de fila del tema pone cursor de mano y fondo al pasar el mouse, y se agrega un chevron «›» al final. Los
/// clics sobre botones, enlaces o cajas de texto dentro de la fila siguen haciendo lo suyo.
/// </summary>
public static class FilaClicable
{
    public const string Chevron = "›";

    public static readonly DependencyProperty ComandoProperty = DependencyProperty.RegisterAttached(
        "Comando", typeof(ICommand), typeof(FilaClicable), new PropertyMetadata(null, AlCambiar));

    /// <summary>Lo pone el comportamiento; el estilo de fila lo usa para el cursor y el fondo.</summary>
    public static readonly DependencyProperty ActivaProperty = DependencyProperty.RegisterAttached(
        "Activa", typeof(bool), typeof(FilaClicable), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>El encabezado de la columna del chevron no muestra texto.</summary>
    private static readonly Style EncabezadoVacio = CrearEncabezadoVacio();

    private static readonly MouseButtonEventHandler ManejadorSoltar = AlSoltar;

    public static ICommand? GetComando(DependencyObject d) => (ICommand?)d.GetValue(ComandoProperty);

    public static void SetComando(DependencyObject d, ICommand? valor) => d.SetValue(ComandoProperty, valor);

    public static bool GetActiva(DependencyObject d) => (bool)d.GetValue(ActivaProperty);

    public static void SetActiva(DependencyObject d, bool valor) => d.SetValue(ActivaProperty, valor);

    /// <summary>Abre el detalle de la fila si el comando lo permite.</summary>
    public static bool Abrir(DataGrid tabla, object? fila)
    {
        if (fila is null || fila == System.Windows.Data.CollectionView.NewItemPlaceholder || GetComando(tabla) is not { } comando || !comando.CanExecute(fila))
        {
            return false;
        }

        comando.Execute(fila);
        return true;
    }

    private static void AlCambiar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid tabla)
        {
            return;
        }

        tabla.PreviewKeyDown -= AlPresionar;
        tabla.RemoveHandler(Mouse.MouseUpEvent, ManejadorSoltar);
        if (e.NewValue is null)
        {
            SetActiva(tabla, false);
            return;
        }

        SetActiva(tabla, true);
        tabla.PreviewKeyDown += AlPresionar;
        // La celda marca el evento como manejado al seleccionar; se escucha igual (los botones se excluyen abajo).
        tabla.AddHandler(Mouse.MouseUpEvent, ManejadorSoltar, handledEventsToo: true);
        if (!tabla.Columns.Any(c => c.Header is string h && h == Chevron))
        {
            tabla.Columns.Add(ColumnaChevron());
        }
    }

    /// <summary>Columna angosta con «›» que indica que la fila abre un detalle.</summary>
    private static DataGridTemplateColumn ColumnaChevron()
    {
        var texto = new FrameworkElementFactory(typeof(TextBlock));
        texto.SetValue(TextBlock.TextProperty, Chevron);
        texto.SetValue(TextBlock.FontSizeProperty, 22.0);
        texto.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        texto.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        texto.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
        texto.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text3");
        texto.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Abrir detalle");
        return new DataGridTemplateColumn
        {
            Header = Chevron,
            HeaderStyle = EncabezadoVacio,
            Width = 36,
            CanUserResize = false,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = texto },
        };
    }

    private static Style CrearEncabezadoVacio()
    {
        var estilo = new Style(typeof(DataGridColumnHeader));
        estilo.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(DataGridColumnHeader)) { VisualTree = new FrameworkElementFactory(typeof(Border)) }));
        return estilo;
    }

    private static void AlPresionar(object sender, KeyEventArgs e)
    {
        // El DataGrid usa Enter para bajar de fila; en estas tablas abre el detalle.
        if (sender is DataGrid tabla && e.Key == Key.Enter && e.OriginalSource is not TextBoxBase && Abrir(tabla, tabla.SelectedItem))
        {
            e.Handled = true;
        }
    }

    private static void AlSoltar(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid tabla || e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject origen || EsControlPropio(origen, tabla)
            || ItemsControl.ContainerFromElement(tabla, origen) is not DataGridRow fila)
        {
            return;
        }

        if (Abrir(tabla, fila.Item))
        {
            e.Handled = true;
        }
    }

    /// <summary>Botones, enlaces, casillas y cajas de texto dentro de la fila no abren el detalle.</summary>
    private static bool EsControlPropio(DependencyObject origen, DataGrid tabla)
    {
        for (var d = origen; d is not null && d != tabla; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBoxBase or System.Windows.Documents.Hyperlink or ComboBox)
            {
                return true;
            }
        }

        return false;
    }
}
