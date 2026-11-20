using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sitbio.Desktop.Controls;

/// <summary>
/// Texto de solo lectura que se ve igual a un TextBlock pero se puede seleccionar y copiar (I-06): placa, serie,
/// nombre del equipo, códigos de orden y tiquete, registro INVIMA y datos de la hoja de vida. Clic derecho
/// «Copiar» copia la selección o, si no hay, todo el texto. No entra en el orden de tabulación.
/// </summary>
public sealed class TextoCopiable : TextBox
{
    static TextoCopiable()
    {
        IsReadOnlyProperty.OverrideMetadata(typeof(TextoCopiable), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(TextoCopiable), new FrameworkPropertyMetadata(false));
        TextWrappingProperty.OverrideMetadata(typeof(TextoCopiable), new FrameworkPropertyMetadata(TextWrapping.Wrap));
    }

    public TextoCopiable()
    {
        var copiar = new MenuItem { Header = "Copiar", InputGestureText = "Ctrl+C" };
        copiar.Click += (_, _) => Copiar();
        ContextMenu = new ContextMenu { Items = { copiar } };
        ContextMenu.SetResourceReference(StyleProperty, "MenuFila");
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            Copiar();
            e.Handled = true;
        }));
    }

    /// <summary>El texto que copiaría ahora: la selección o, si no hay, todo.</summary>
    public string TextoParaCopiar => string.IsNullOrEmpty(SelectedText) ? Text : SelectedText;

    public void Copiar() => Portapapeles.Copiar(TextoParaCopiar);
}
