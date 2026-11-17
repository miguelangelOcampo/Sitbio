using System.Windows;
using System.Windows.Controls;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-003 Panel de inicio. La lógica está en PanelViewModel.</summary>
public partial class PanelView : UserControl
{
    public PanelView() => InitializeComponent();

    /// <summary>Marca la pestaña del periodo vigente al dibujarse (las pestañas son de solo comando).</summary>
    private void Periodo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: OpcionPeriodo opcion } boton && DataContext is PanelViewModel vm)
        {
            boton.IsChecked = opcion.Clave == vm.Periodo;
        }
    }
}
