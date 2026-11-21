using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sitbio.Desktop.Views;

/// <summary>J-12: avatar redondo con la foto de perfil (<see cref="Miniatura"/>) o, si no hay, las iniciales.</summary>
public partial class Avatar : UserControl
{
    public static readonly DependencyProperty MiniaturaProperty = DependencyProperty.Register(
        nameof(Miniatura), typeof(byte[]), typeof(Avatar), new PropertyMetadata(null, (d, _) => ((Avatar)d).Pintar()));

    public static readonly DependencyProperty InicialesProperty = DependencyProperty.Register(
        nameof(Iniciales), typeof(string), typeof(Avatar), new PropertyMetadata(string.Empty));

    /// <summary>Color del círculo detrás de las iniciales (no se usa Background, que pintaría un cuadrado).</summary>
    public static readonly DependencyProperty RellenoProperty = DependencyProperty.Register(
        nameof(Relleno), typeof(Brush), typeof(Avatar), new PropertyMetadata(null));

    private static readonly BytesImagenConverter Conversor = new();

    public Avatar() => InitializeComponent();

    public byte[]? Miniatura
    {
        get => (byte[]?)GetValue(MiniaturaProperty);
        set => SetValue(MiniaturaProperty, value);
    }

    public string Iniciales
    {
        get => (string)GetValue(InicialesProperty);
        set => SetValue(InicialesProperty, value);
    }

    public Brush? Relleno
    {
        get => (Brush?)GetValue(RellenoProperty);
        set => SetValue(RellenoProperty, value);
    }

    /// <summary>True cuando se ve la foto y no las iniciales.</summary>
    public bool MuestraFoto => Imagen.Visibility == Visibility.Visible;

    private void Pintar()
    {
        if (Conversor.Convert(Miniatura, typeof(ImageSource), null, CultureInfo.InvariantCulture) is BitmapSource foto)
        {
            Imagen.Fill = new ImageBrush(foto) { Stretch = Stretch.UniformToFill };
            Imagen.Visibility = Visibility.Visible;
            TextoIniciales.Visibility = Visibility.Collapsed;
        }
        else
        {
            Imagen.Fill = null;
            Imagen.Visibility = Visibility.Collapsed;
            TextoIniciales.Visibility = Visibility.Visible;
        }
    }
}
