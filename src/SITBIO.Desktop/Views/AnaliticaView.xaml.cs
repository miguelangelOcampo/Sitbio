using System.Windows.Controls;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>UI-023 Analítica. La lógica está en AnaliticaViewModel; aquí solo se capturan los gráficos para el PDF.</summary>
public partial class AnaliticaView : UserControl
{
    public AnaliticaView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AnaliticaViewModel vm)
            {
                vm.CapturarGraficos = () => CapturaGraficos.Capturar(this);
            }
        };
    }
}
