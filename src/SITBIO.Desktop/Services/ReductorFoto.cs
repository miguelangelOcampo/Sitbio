using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Foto lista para subir (RF-031): miniatura cuadrada de 128 px recortada al centro y vista de hasta 512 px en el
/// lado mayor, sin recortar. Las dos en JPEG: WPF no trae codificador WebP y el DOC-06 admite JPEG como alternativa.
/// </summary>
public sealed record FotoReducida(byte[] Miniatura, byte[] Vista, int AnchoVista, int AltoVista)
{
    public const string TipoMimeJpeg = "image/jpeg";

    public string TipoMime => TipoMimeJpeg;

    public string Extension => ".jpg";

    public long TamanoBytes => Miniatura.LongLength + Vista.LongLength;
}

/// <summary>Reduce la foto en el cliente antes de enviarla: el original del celular o la cámara nunca se sube.</summary>
public interface IReductorFoto
{
    /// <summary>Lanza <see cref="ImagenNoValida"/> si el contenido no es una imagen que Windows pueda leer.</summary>
    FotoReducida Reducir(byte[] original);
}

public sealed class ImagenNoValida() : Exception("No pudimos leer la imagen.");

/// <summary>
/// Reducción con WIC (System.Windows.Media.Imaging): lee JPG, PNG y WebP (códec de Windows) y escribe JPEG.
/// Se dibuja sobre fondo blanco a la medida exacta, así un PNG transparente no queda negro. Usa el hilo de la interfaz (STA).
/// </summary>
public sealed class ReductorFotoWpf : IReductorFoto
{
    public const int LadoMiniatura = 128;
    public const int LadoMaximoVista = 512;

    /// <summary>Calidad JPEG 85: buena nitidez con pocas decenas de KB por foto.</summary>
    public const int CalidadJpeg = 85;

    public FotoReducida Reducir(byte[] original)
    {
        var imagen = Decodificar(original);
        int ancho = imagen.PixelWidth, alto = imagen.PixelHeight;

        // Miniatura: cuadrado central escalado a 128 × 128.
        var lado = Math.Min(ancho, alto);
        var cuadrado = new Rect((ancho - lado) / 2d, (alto - lado) / 2d, lado, lado);
        var miniatura = Dibujar(imagen, cuadrado, LadoMiniatura, LadoMiniatura);

        // Vista: lado mayor de 512 px como máximo; nunca se amplía.
        var factor = Math.Min(1d, (double)LadoMaximoVista / Math.Max(ancho, alto));
        var anchoVista = Math.Max(1, (int)Math.Round(ancho * factor));
        var altoVista = Math.Max(1, (int)Math.Round(alto * factor));
        var vista = Dibujar(imagen, new Rect(0, 0, ancho, alto), anchoVista, altoVista);

        return new FotoReducida(Jpeg(miniatura), Jpeg(vista), anchoVista, altoVista);
    }

    private static BitmapSource Decodificar(byte[] original)
    {
        try
        {
            using var flujo = new MemoryStream(original, writable: false);
            var decodificador = BitmapDecoder.Create(flujo, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var cuadro = decodificador.Frames[0];
            if (cuadro.PixelWidth < 1 || cuadro.PixelHeight < 1)
            {
                throw new ImagenNoValida();
            }

            cuadro.Freeze();
            return cuadro;
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException or ArgumentException or IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new ImagenNoValida();
        }
    }

    /// <summary>Dibuja la parte <paramref name="origen"/> de la imagen (en píxeles) en un lienzo de ancho × alto exactos.</summary>
    private static BitmapSource Dibujar(BitmapSource imagen, Rect origen, int ancho, int alto)
    {
        // Las coordenadas de dibujo de WPF son de 96 ppp: se pasa de píxeles a unidades de la imagen.
        var escalaX = imagen.Width / imagen.PixelWidth;
        var escalaY = imagen.Height / imagen.PixelHeight;
        var escala = new ScaleTransform(ancho / (origen.Width * escalaX), alto / (origen.Height * escalaY));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dibujo = visual.RenderOpen())
        {
            dibujo.DrawRectangle(Brushes.White, null, new Rect(0, 0, ancho, alto));
            dibujo.PushTransform(escala);
            dibujo.PushTransform(new TranslateTransform(-origen.X * escalaX, -origen.Y * escalaY));
            dibujo.DrawImage(imagen, new Rect(0, 0, imagen.Width, imagen.Height));
        }

        var lienzo = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        lienzo.Render(visual);
        var resultado = new FormatConvertedBitmap(lienzo, PixelFormats.Bgr24, null, 0);
        resultado.Freeze();
        return resultado;
    }

    private static byte[] Jpeg(BitmapSource imagen)
    {
        var codificador = new JpegBitmapEncoder { QualityLevel = CalidadJpeg };
        codificador.Frames.Add(BitmapFrame.Create(imagen));
        using var salida = new MemoryStream();
        codificador.Save(salida);
        return salida.ToArray();
    }
}
