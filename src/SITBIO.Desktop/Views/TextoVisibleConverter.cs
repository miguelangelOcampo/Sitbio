using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sitbio.Desktop.Views;

/// <summary>Visible si el texto no está vacío; si no, Collapsed.</summary>
public sealed class TextoVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string texto && texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
