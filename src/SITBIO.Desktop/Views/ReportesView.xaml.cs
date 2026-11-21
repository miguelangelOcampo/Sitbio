using System.Windows.Controls;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-021 Reportes e indicadores. La lógica está en ReportesViewModel; aquí solo se capturan los gráficos para el PDF.</summary>
public partial class ReportesView : UserControl
{
    public ReportesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ReportesViewModel vm)
            {
                vm.CapturarGraficos = () => CapturaGraficos.Capturar(this);
            }
        };
    }
}
