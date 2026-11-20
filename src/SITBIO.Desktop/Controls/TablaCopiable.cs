using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Sitbio.Desktop.Controls;

/// <summary>
/// Copia desde las tablas (I-06): Ctrl + C copia el texto de la celda actual y Ctrl + Mayús + C la fila completa
/// (columnas separadas por tabulación, para pegar en Excel). El clic derecho ofrece «Copiar celda» y «Copiar fila».
/// Se activa en el estilo OpenGrid, así que aplica a todas las tablas.
/// </summary>
public static class TablaCopiable
{
    public static readonly DependencyProperty ActivaProperty = DependencyProperty.RegisterAttached(
        "Activa", typeof(bool), typeof(TablaCopiable), new PropertyMetadata(false, AlCambiar));

    public static bool GetActiva(DependencyObject d) => (bool)d.GetValue(ActivaProperty);

    public static void SetActiva(DependencyObject d, bool valor) => d.SetValue(ActivaProperty, valor);

    /// <summary>Texto visible de una celda (TextBlock, Run y cajas de texto), sin espacios repetidos.</summary>
    public static string TextoCelda(DataGrid tabla, object? fila, DataGridColumn? columna)
    {
        if (fila is null || columna is null || columna.GetCellContent(fila) is not { } contenido)
        {
            return string.Empty;
        }

        var partes = Textos(contenido).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        return string.Join(" ", partes);
    }

    /// <summary>Texto de la fila: una columna por tabulación, en el orden en que se ven.</summary>
    public static string TextoFila(DataGrid tabla, object? fila) => fila is null
        ? string.Empty
        : string.Join('\t', tabla.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).Select(c => TextoCelda(tabla, fila, c)));

    public static void CopiarCelda(DataGrid tabla) => Portapapeles.Copiar(TextoCelda(tabla, FilaActual(tabla), tabla.CurrentCell.Column));

    public static void CopiarFila(DataGrid tabla) => Portapapeles.Copiar(TextoFila(tabla, FilaActual(tabla)));

    /// <summary>La fila de la celda actual o la seleccionada; null si no hay ninguna.</summary>
    private static object? FilaActual(DataGrid tabla) =>
        tabla.CurrentCell.Item is { } item && item != DependencyProperty.UnsetValue ? item : tabla.SelectedItem;

    private static void AlCambiar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid tabla || e.NewValue is not true)
        {
            return;
        }

        tabla.PreviewKeyDown += AlPresionar;
        tabla.PreviewMouseRightButtonDown += AlClicDerecho;
        var celda = new MenuItem { Header = "Copiar celda", InputGestureText = "Ctrl+C" };
        celda.Click += (_, _) => CopiarCelda(tabla);
        var fila = new MenuItem { Header = "Copiar fila", InputGestureText = "Ctrl+Mayús+C" };
        fila.Click += (_, _) => CopiarFila(tabla);
        if (tabla.ContextMenu is null)
        {
            tabla.ContextMenu = new ContextMenu { Items = { celda, fila } };
            tabla.ContextMenu.SetResourceReference(FrameworkElement.StyleProperty, "MenuFila");
        }
    }

    private static void AlPresionar(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid tabla || e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.OriginalSource is TextBox)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            CopiarFila(tabla);
        }
        else
        {
            CopiarCelda(tabla);
        }

        e.Handled = true;
    }

    /// <summary>El clic derecho elige la celda bajo el mouse para que el menú copie esa.</summary>
    private static void AlClicDerecho(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid tabla && Ancestro<DataGridCell>(e.OriginalSource as DependencyObject) is { } celda)
        {
            tabla.CurrentCell = new DataGridCellInfo(celda);
        }
    }

    private static IEnumerable<string> Textos(DependencyObject elemento)
    {
        switch (elemento)
        {
            case TextBlock bloque when bloque.IsVisible || !bloque.IsLoaded:
                yield return bloque.Inlines.Count > 0 ? new TextRange(bloque.ContentStart, bloque.ContentEnd).Text : bloque.Text;
                yield break;
            case TextBox caja:
                yield return caja.Text;
                yield break;
            case FrameworkElement { IsLoaded: true, IsVisible: false }:
                yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(elemento); i++)
        {
            foreach (var texto in Textos(VisualTreeHelper.GetChild(elemento, i)))
            {
                yield return texto;
            }
        }
    }

    private static T? Ancestro<T>(DependencyObject? d)
        where T : DependencyObject
    {
        while (d is not null and not T)
        {
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return d as T;
    }
}
