using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Sitbio.Desktop.Views;

/// <summary>Muestra en un Image los bytes de una foto (RF-031); null o una imagen dañada no muestran nada.</summary>
public sealed class BytesImagenConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.StreamSource = new MemoryStream(bytes, writable: false);
            imagen.EndInit();
            imagen.Freeze();
            return imagen;
        }
        catch (Exception error) when (error is NotSupportedException or IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
