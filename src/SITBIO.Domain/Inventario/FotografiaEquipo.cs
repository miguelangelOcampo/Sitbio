using System.Buffers.Binary;

namespace Sitbio.Domain.Inventario;

/// <summary>Formato y tamaño en píxeles de una imagen, leídos de su cabecera.</summary>
public readonly record struct ImagenFoto(string TipoMime, int Ancho, int Alto)
{
    public int LadoMayor => Math.Max(Ancho, Alto);
}

/// <summary>
/// Fotografía del equipo (RF-031, CA-HU-015-03): el cliente la reduce a una miniatura de 128 × 128 px
/// (recorte centrado) y a una vista de hasta 512 px en el lado mayor; el original nunca se envía.
/// La API comprueba la firma y las dimensiones de cada versión leyendo la cabecera (JPG, PNG o WebP),
/// de modo que una imagen sin reducir no se pueda guardar.
/// </summary>
public static class FotografiaEquipo
{
    public const int LadoMiniatura = 128;
    public const int LadoMaximoVista = 512;

    /// <summary>Tope por versión ya reducida; la entrada del cliente admite hasta 10 MB antes de reducirla.</summary>
    public const long TamanoMaximoBytes = 2L * 1024 * 1024;

    public const string NombreArchivo = "Fotografía del equipo";

    /// <summary>Extensión en disco de la imagen (.jpg, .png o .webp).</summary>
    public static string Extension(string tipoMime) => tipoMime switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(tipoMime), tipoMime, "Formato de imagen no permitido."),
    };

    /// <summary>Ruta de la miniatura junto a la vista: <c>&lt;id&gt;.webp</c> → <c>&lt;id&gt;-128.webp</c>.</summary>
    public static string RutaMiniatura(string rutaVista)
    {
        var extension = Path.GetExtension(rutaVista);
        return string.Concat(rutaVista.AsSpan(0, rutaVista.Length - extension.Length), "-", LadoMiniatura.ToString(System.Globalization.CultureInfo.InvariantCulture), extension);
    }

    /// <summary>Formato y dimensiones de una imagen JPG, PNG o WebP; null si no es una de ellas o la cabecera está dañada.</summary>
    public static ImagenFoto? Leer(ReadOnlySpan<byte> datos)
    {
        try
        {
            return LeerPng(datos) ?? LeerWebp(datos) ?? LeerJpeg(datos);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ImagenFoto? LeerPng(ReadOnlySpan<byte> d)
    {
        ReadOnlySpan<byte> firma = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 24 || !d.StartsWith(firma) || !d.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        return Valida("image/png", BinaryPrimitives.ReadInt32BigEndian(d.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(d.Slice(20, 4)));
    }

    private static ImagenFoto? LeerWebp(ReadOnlySpan<byte> d)
    {
        if (d.Length < 30 || !d.StartsWith("RIFF"u8) || !d.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var bloque = d.Slice(12, 4);
        if (bloque.SequenceEqual("VP8 "u8))
        {
            // Trama clave: código de inicio 9D 01 2A y luego ancho y alto de 14 bits.
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A)
            {
                return null;
            }

            return Valida("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26, 2)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28, 2)) & 0x3FFF);
        }

        if (bloque.SequenceEqual("VP8L"u8))
        {
            if (d[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(21, 4));
            return Valida("image/webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (bloque.SequenceEqual("VP8X"u8))
        {
            var ancho = d[24] | (d[25] << 8) | (d[26] << 16);
            var alto = d[27] | (d[28] << 8) | (d[29] << 16);
            return Valida("image/webp", ancho + 1, alto + 1);
        }

        return null;
    }

    private static ImagenFoto? LeerJpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        var i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null;
            }

            var marcador = d[i + 1];
            if (marcador == 0xFF)
            {
                i++;
                continue;
            }

            var longitud = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2, 2));
            // SOF0..SOF15 (sin DHT C4, JPG C8 ni DAC CC): alto y ancho de la imagen.
            if (marcador is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return Valida("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7, 2)), BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2)));
            }

            if (marcador == 0xDA || longitud < 2)
            {
                return null;
            }

            i += 2 + longitud;
        }

        return null;
    }

    private static ImagenFoto? Valida(string mime, int ancho, int alto) =>
        ancho > 0 && alto > 0 ? new ImagenFoto(mime, ancho, alto) : null;
}
