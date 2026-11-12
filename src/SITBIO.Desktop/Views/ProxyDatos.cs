using System.Windows;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Puente para enlazar propiedades del ViewModel desde elementos que no están en el árbol visual,
/// como las columnas de un DataGrid (por ejemplo, su visibilidad).
/// </summary>
public sealed class ProxyDatos : Freezable
{
    public static readonly DependencyProperty DatosProperty =
        DependencyProperty.Register(nameof(Datos), typeof(object), typeof(ProxyDatos));

    public object? Datos
    {
        get => GetValue(DatosProperty);
        set => SetValue(DatosProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new ProxyDatos();
}
