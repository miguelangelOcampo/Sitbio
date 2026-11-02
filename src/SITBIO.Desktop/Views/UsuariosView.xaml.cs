using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Sitbio.Desktop.Views;

public partial class UsuariosView : UserControl
{
    // Ancho mínimo del menú de la fila (estilo MenuFila).
    private const double AnchoMenu = 200;

    public UsuariosView() => InitializeComponent();

    // Abre el menú de acciones de la fila debajo del botón «⋯» (solo presentación).
    private void AbrirMenuFila(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } boton)
        {
            menu.PlacementTarget = boton;
            menu.Placement = PlacementMode.Bottom;
            // Alineado al borde derecho del botón para que no se salga de la ventana.
            menu.HorizontalOffset = boton.ActualWidth - AnchoMenu;
            menu.IsOpen = true;
        }
    }
}
