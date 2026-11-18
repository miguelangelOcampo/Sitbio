using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

/// <summary>RF-031: el cliente reduce la foto a 128 × 128 (recorte central) y a 512 px como máximo antes de subirla.</summary>
public class ReductorFotoTests
{
    private readonly ReductorFotoWpf _reductor = new();

    /// <summary>WPF dibuja solo en hilos STA; xUnit corre en MTA.</summary>
    private static T EnSta<T>(Func<T> accion)
    {
        T resultado = default!;
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                resultado = accion();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null)
        {
            throw error;
        }

        return resultado;
    }

    /// <summary>PNG de ancho × alto: mitad izquierda roja y derecha azul (o transparente).</summary>
    private static byte[] Png(int ancho, int alto, bool transparente = false)
    {
        var pixeles = new byte[ancho * alto * 4];
        for (var y = 0; y < alto; y++)
        {
            for (var x = 0; x < ancho; x++)
            {
                var i = ((y * ancho) + x) * 4;
                var izquierda = x < ancho / 2;
                pixeles[i] = (byte)(izquierda ? 0 : 255);      // B
                pixeles[i + 1] = 0;                            // G
                pixeles[i + 2] = (byte)(izquierda ? 255 : 0);  // R
                pixeles[i + 3] = (byte)(transparente ? 0 : 255);
            }
        }

        var imagen = BitmapSource.Create(ancho, alto, 96, 96, PixelFormats.Bgra32, null, pixeles, ancho * 4);
        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(imagen));
        using var salida = new MemoryStream();
        codificador.Save(salida);
        return salida.ToArray();
    }

    private static BitmapFrame Leer(byte[] jpeg)
    {
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        return BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static Color Pixel(BitmapSource imagen, int x, int y)
    {
        var bgr = new FormatConvertedBitmap(imagen, PixelFormats.Bgr32, null, 0);
        var p = new byte[4];
        bgr.CopyPixels(new Int32Rect(x, y, 1, 1), p, 4, 0);
        return Color.FromRgb(p[2], p[1], p[0]);
    }

    [Fact]
    public void FotoGrande_MiniaturaDe128x128YVistaDe512EnElLadoMayor_EnJpeg()
    {
        var (mini, vista, foto) = EnSta(() =>
        {
            var foto = _reductor.Reducir(Png(1600, 900));
            var mini = Leer(foto.Miniatura);
            var vista = Leer(foto.Vista);
            return ((mini.PixelWidth, mini.PixelHeight), (vista.PixelWidth, vista.PixelHeight), foto);
        });

        Assert.Equal((128, 128), mini);
        Assert.Equal((512, 288), vista);
        Assert.Equal((512, 288), (foto.AnchoVista, foto.AltoVista));
        Assert.Equal("image/jpeg", foto.TipoMime);
        Assert.True(foto.TamanoBytes < 100 * 1024, $"{foto.TamanoBytes} bytes");
    }

    [Fact]
    public void LaMiniaturaRecortaElCentro_YLaVistaConservaTodaLaImagen()
    {
        var (miniIzq, miniDer, vistaIzq, vistaDer) = EnSta(() =>
        {
            var foto = _reductor.Reducir(Png(1600, 400));
            var mini = Leer(foto.Miniatura);
            var vista = Leer(foto.Vista);
            return (Pixel(mini, 10, 64), Pixel(mini, 118, 64), Pixel(vista, 5, 64), Pixel(vista, 506, 64));
        });

        // El cuadrado central de 400 px cae justo en el cambio de color: rojo a la izquierda, azul a la derecha.
        Assert.True(miniIzq.R > 200 && miniIzq.B < 60, miniIzq.ToString());
        Assert.True(miniDer.B > 200 && miniDer.R < 60, miniDer.ToString());
        Assert.True(vistaIzq.R > 200 && vistaDer.B > 200);
    }

    [Fact]
    public void FotoPequena_NoSeAmplia()
    {
        var vista = EnSta(() =>
        {
            var v = Leer(_reductor.Reducir(Png(300, 200)).Vista);
            return (v.PixelWidth, v.PixelHeight);
        });

        Assert.Equal((300, 200), vista);
    }

    [Fact]
    public void PngTransparente_QuedaSobreFondoBlanco()
    {
        var pixel = EnSta(() => Pixel(Leer(_reductor.Reducir(Png(200, 200, transparente: true)).Miniatura), 64, 64));

        Assert.True(pixel.R > 240 && pixel.G > 240 && pixel.B > 240, pixel.ToString());
    }

    [Fact]
    public void ArchivoQueNoEsImagen_ImagenNoValida()
    {
        var error = EnSta(() => Record.Exception(() => _reductor.Reducir("no soy una imagen"u8.ToArray())));

        Assert.IsType<ImagenNoValida>(error);
    }
}
