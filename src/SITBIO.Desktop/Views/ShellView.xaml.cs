using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
        PreviewKeyDown += AlPresionarTecla;

        // Como en el prototipo, salir de la caja la limpia y cierra el popup (los resultados no toman el foco).
        BusquedaGlobalTexto.LostKeyboardFocus += (_, _) =>
        {
            if (DataContext is ShellViewModel { BusquedaGlobal: { } busqueda })
            {
                busqueda.CerrarCommand.Execute(null);
            }
        };
    }

    /// <summary>D-04: Ctrl + K lleva el foco a la búsqueda global desde cualquier pantalla.</summary>
    private void AlPresionarTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control && BusquedaGlobalTexto.IsVisible)
        {
            BusquedaGlobalTexto.Focus();
            BusquedaGlobalTexto.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Clic en un resultado del popup: abre su detalle.</summary>
    private void AlElegirResultado(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ResultadoEncontrado resultado }
            && DataContext is ShellViewModel { BusquedaGlobal: { } busqueda })
        {
            busqueda.AbrirCommand.Execute(resultado);
            e.Handled = true;
        }
    }
}
