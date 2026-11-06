using System.Windows.Controls;
using System.Windows.Input;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-005 Equipos biomédicos y UI-006 Hoja de vida. La lógica está en EquiposViewModel.</summary>
public partial class EquiposView : UserControl
{
    public EquiposView() => InitializeComponent();

    private void TablaEquipos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Solo el doble clic sobre una fila (no sobre el encabezado ni el espacio vacío) abre la hoja de vida.
        if (e.OriginalSource is System.Windows.DependencyObject origen && ItemsControl.ContainerFromElement(TablaEquipos, origen) is DataGridRow)
        {
            Abrir();
        }
    }

    private void TablaEquipos_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // El DataGrid usa Enter para bajar de fila; aquí abre la hoja de vida (NAV-006).
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Abrir();
        }
    }

    private void Abrir()
    {
        if (DataContext is EquiposViewModel vm && TablaEquipos.SelectedItem is FilaEquipo fila)
        {
            vm.AbrirCommand.Execute(fila);
        }
    }
}
