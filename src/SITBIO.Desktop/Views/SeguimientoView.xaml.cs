using System.Windows.Controls;
using System.Windows.Input;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-010 Seguimiento de tiquetes. La lógica está en SeguimientoViewModel.</summary>
public partial class SeguimientoView : UserControl
{
    public SeguimientoView() => InitializeComponent();

    private void TablaTiquetes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Solo el doble clic sobre una fila (no sobre el encabezado ni el espacio vacío) abre el detalle.
        if (e.OriginalSource is System.Windows.DependencyObject origen && ItemsControl.ContainerFromElement(TablaTiquetes, origen) is DataGridRow)
        {
            Abrir();
        }
    }

    private void TablaTiquetes_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // El DataGrid usa Enter para bajar de fila; aquí abre el detalle (NAV-006).
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Abrir();
        }
    }

    private void Abrir()
    {
        if (DataContext is SeguimientoViewModel vm && TablaTiquetes.SelectedItem is FilaTiquete fila)
        {
            vm.AbrirCommand.Execute(fila);
        }
    }
}
