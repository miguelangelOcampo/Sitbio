using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
        PreviewKeyDown += AlPresionarTecla;

        // I-14: al entrar se consulta el punto de aviso de Solicitudes (tiquetes sin técnico).
        Loaded += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.CargarAvisosCommand.Execute(null);
            }
        };

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
    /// <summary>H-03: el menú del avatar se abre bajo el botón, alineado a su borde derecho.</summary>
    private void AbrirMenuUsuario(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } boton)
        {
            menu.PlacementTarget = boton;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = boton.ActualWidth - Math.Max(menu.MinWidth, menu.ActualWidth);
            menu.IsOpen = true;
        }
    }

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
