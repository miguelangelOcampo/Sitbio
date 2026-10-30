using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sitbio.Desktop.Views;

/// <summary>Convierte una clave (por ejemplo «Icono.Riel.Inicio») en el recurso de la aplicación.</summary>
public sealed class RecursoConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string clave ? Application.Current.TryFindResource(clave) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
