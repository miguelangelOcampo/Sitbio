using System.Windows;
using System.Windows.Media;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Ícono antes del texto de un botón, como en el prototipo: lo dibujan las plantillas de PrimaryButton (y DangerButton)
/// y LinkButton con el color del texto. Uso: <c>views:Boton.Icono="{StaticResource Icono.Descargar}"</c>.
/// </summary>
public static class Boton
{
    public static readonly DependencyProperty IconoProperty = DependencyProperty.RegisterAttached(
        "Icono", typeof(Geometry), typeof(Boton), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static Geometry? GetIcono(DependencyObject elemento)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        return (Geometry?)elemento.GetValue(IconoProperty);
    }

    public static void SetIcono(DependencyObject elemento, Geometry? valor)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        elemento.SetValue(IconoProperty, valor);
    }
}
