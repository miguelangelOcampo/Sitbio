using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Sitbio.Desktop.Views;

/// <summary>UI-005 Equipos biomédicos y UI-006 Hoja de vida. La lógica está en EquiposViewModel.</summary>
public partial class EquiposView : UserControl
{
    public EquiposView() => InitializeComponent();

    private const double AnchoMenu = 200;

    /// <summary>Menú «…» de la hoja de vida, alineado al borde derecho del botón para que no se salga de la ventana.</summary>
    private void AbrirMenuAcciones(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } boton)
        {
            menu.PlacementTarget = boton;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = boton.ActualWidth - AnchoMenu;
            menu.IsOpen = true;
        }
    }
}
