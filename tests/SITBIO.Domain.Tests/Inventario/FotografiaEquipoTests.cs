using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

/// <summary>RF-031 (CA-HU-015-03): formato y dimensiones leídos de la cabecera de JPG, PNG y WebP.</summary>
public class FotografiaEquipoTests
{
    public static byte[] Png(int ancho, int alto)
    {
        byte[] d = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), ancho);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(20), alto);
        return d;
    }

    public static byte[] Jpeg(int ancho, int alto) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(alto >> 8), (byte)alto, (byte)(ancho >> 8), (byte)ancho, 0x03, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1,
    ];

    public static byte[] WebpVp8X(int ancho, int alto)
    {
        var d = new byte[30];
        "RIFF"u8.CopyTo(d);
        "WEBPVP8X"u8.CopyTo(d.AsSpan(8));
        d[16] = 10;
        d[24] = (byte)(ancho - 1);
        d[25] = (byte)((ancho - 1) >> 8);
        d[27] = (byte)(alto - 1);
        d[28] = (byte)((alto - 1) >> 8);
        return d;
    }

    private static byte[] WebpVp8L(int ancho, int alto)
    {
        var d = new byte[30];
        "RIFF"u8.CopyTo(d);
        "WEBPVP8L"u8.CopyTo(d.AsSpan(8));
        d[20] = 0x2F;
        var bits = (uint)(ancho - 1) | ((uint)(alto - 1) << 14);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(21), bits);
        return d;
    }

    private static byte[] WebpVp8(int ancho, int alto)
    {
        var d = new byte[30];
        "RIFF"u8.CopyTo(d);
        "WEBPVP8 "u8.CopyTo(d.AsSpan(8));
        d[23] = 0x9D;
        d[24] = 0x01;
        d[25] = 0x2A;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(26), (ushort)ancho);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(28), (ushort)alto);
        return d;
    }

    public static TheoryData<byte[], string, int, int> Imagenes => new()
    {
        { Png(512, 384), "image/png", 512, 384 },
        { Jpeg(300, 512), "image/jpeg", 300, 512 },
        { WebpVp8X(128, 128), "image/webp", 128, 128 },
        { WebpVp8L(512, 341), "image/webp", 512, 341 },
        { WebpVp8(400, 300), "image/webp", 400, 300 },
    };

    [Theory]
    [MemberData(nameof(Imagenes))]
    public void Leer_DevuelveFormatoYDimensiones(byte[] datos, string mime, int ancho, int alto) =>
        Assert.Equal(new ImagenFoto(mime, ancho, alto), FotografiaEquipo.Leer(datos));

    [Fact]
    public void Leer_ContenidoQueNoEsImagenOCabeceraIncompleta_DevuelveNull()
    {
        Assert.Null(FotografiaEquipo.Leer("%PDF-1.7\n%%EOF"u8));
        Assert.Null(FotografiaEquipo.Leer(Png(128, 128).AsSpan(0, 20)));
        Assert.Null(FotografiaEquipo.Leer([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Null(FotografiaEquipo.Leer(Png(0, 128)));
    }

    [Fact]
    public void RutaMiniatura_VaJuntoALaVista() =>
        Assert.Equal("t/e/abc-128.webp", FotografiaEquipo.RutaMiniatura("t/e/abc.webp"));

    [Fact]
    public void AsignarYQuitarFoto_CambianElDocumentoDelEquipo()
    {
        var equipo = new EquipoBiomedico(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SN-001", "PL-001", "Dräger", "Fabius plus", DateTimeOffset.UtcNow);
        var documento = Guid.NewGuid();

        equipo.AsignarFoto(documento);
        Assert.Equal(documento, equipo.FotoDocumentoId);

        equipo.QuitarFoto();
        Assert.Null(equipo.FotoDocumentoId);
        Assert.Throws<ArgumentException>(() => equipo.AsignarFoto(Guid.Empty));
    }
}
