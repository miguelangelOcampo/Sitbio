using System.Windows.Controls;
using System.Windows.Input;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-011 Bandeja de tiquetes y asignación. La lógica está en BandejaViewModel.</summary>
public partial class BandejaView : UserControl
{
    public BandejaView() => InitializeComponent();

    private void TablaBandeja_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Solo el doble clic sobre una fila (no sobre el encabezado ni el espacio vacío) abre el panel.
        if (e.OriginalSource is System.Windows.DependencyObject origen && ItemsControl.ContainerFromElement(TablaBandeja, origen) is DataGridRow)
        {
            Abrir();
        }
    }

    private void TablaBandeja_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // El DataGrid usa Enter para bajar de fila; aquí abre el panel de asignación (NAV-008).
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Abrir();
        }
    }

    private void Abrir()
    {
        if (DataContext is BandejaViewModel vm && TablaBandeja.SelectedItem is FilaBandeja fila)
        {
            vm.AbrirAsignacionCommand.Execute(fila);
        }
    }
}
