using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Documento de soporte de la hoja de vida de un equipo (tabla <c>documento_equipo</c>, script 01 + V010;
/// RF-015, HU-015): manual, certificado, protocolo, ficha técnica, fotografía u otro. Guarda la ruta del
/// archivo, no el binario. No se borra: se desactiva (deja de mostrarse) y el archivo se conserva.
/// </summary>
public sealed class DocumentoEquipo : EntidadTenant
{
    public const int LongitudMaximaNombre = 150;
    public const int LongitudMaximaTipo = 60;

    // Constructor para EF Core.
    private DocumentoEquipo(Guid tenantId)
        : base(tenantId)
    {
    }

    public DocumentoEquipo(
        Guid tenantId,
        Guid equipoId,
        Guid subidoPorId,
        string tipoDocumento,
        string nombreArchivo,
        string tipoMime,
        long tamanoBytes,
        DateTimeOffset fechaCarga)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("El documento debe ser de un equipo.", nameof(equipoId));
        SubidoPorId = subidoPorId != Guid.Empty ? subidoPorId : throw new ArgumentException("El documento debe tener quién lo subió.", nameof(subidoPorId));
        TipoDocumento = TiposDocumento.Todos.Contains(tipoDocumento) ? tipoDocumento : throw new ArgumentException("Tipo de documento no permitido.", nameof(tipoDocumento));
        NombreArchivo = !string.IsNullOrWhiteSpace(nombreArchivo) && nombreArchivo.Length <= LongitudMaximaNombre
            ? nombreArchivo
            : throw new ArgumentException($"El nombre del archivo va de 1 a {LongitudMaximaNombre} caracteres.", nameof(nombreArchivo));
        TipoMime = tipoMime;
        TamanoBytes = tamanoBytes > 0 ? tamanoBytes : throw new ArgumentException("El archivo está vacío.", nameof(tamanoBytes));
        FechaCarga = fechaCarga;
        RutaAlmacenamiento = string.Empty;
    }

    public Guid EquipoId { get; private set; }

    /// <summary>Quién lo subió (sale del token, nunca del cuerpo de la petición).</summary>
    public Guid SubidoPorId { get; private set; }

    public string TipoDocumento { get; private set; } = string.Empty;

    /// <summary>Nombre original, solo para mostrar y descargar; en disco se usa el id (<see cref="RutaAlmacenamiento"/>).</summary>
    public string NombreArchivo { get; private set; } = string.Empty;

    /// <summary>Ruta relativa a <c>Storage:AttachmentsPath</c>: <c>&lt;tenant&gt;/&lt;equipo&gt;/&lt;id&gt;.&lt;ext&gt;</c>.</summary>
    public string RutaAlmacenamiento { get; private set; } = string.Empty;

    public string TipoMime { get; private set; } = string.Empty;

    public long TamanoBytes { get; private set; }

    public DateTimeOffset FechaCarga { get; private set; }

    public bool EstadoActivo { get; private set; } = true;

    public DateTimeOffset? FechaDesactivacion { get; private set; }

    public Guid? DesactivadoPorId { get; private set; }

    /// <summary>Ruta donde quedó el archivo; se fija una sola vez, antes de guardar el registro.</summary>
    public void UbicarEn(string rutaRelativa)
    {
        if (RutaAlmacenamiento.Length > 0)
        {
            throw new InvalidOperationException("El documento ya tiene su archivo.");
        }

        RutaAlmacenamiento = !string.IsNullOrWhiteSpace(rutaRelativa) ? rutaRelativa : throw new ArgumentException("Falta la ruta del archivo.", nameof(rutaRelativa));
    }

    /// <summary>Desactivación lógica (HU-015): deja de mostrarse; el registro y el archivo se conservan.</summary>
    public void Desactivar(Guid usuarioId, DateTimeOffset fecha)
    {
        if (!EstadoActivo)
        {
            throw new InvalidOperationException("El documento ya está desactivado.");
        }

        DesactivadoPorId = usuarioId != Guid.Empty ? usuarioId : throw new ArgumentException("Falta quién desactiva el documento.", nameof(usuarioId));
        EstadoActivo = false;
        FechaDesactivacion = fecha;
    }
}

/// <summary>Tipos de documento de la hoja de vida (prototipo, pestaña Documentos de UI-006).</summary>
public static class TiposDocumento
{
    public const string Fotografia = "Fotografía";

    public static IReadOnlyList<string> Todos { get; } =
    [
        "Manual de usuario",
        "Certificado de calibración",
        "Protocolo de mantenimiento",
        "Ficha técnica",
        Fotografia,
        "Otro",
    ];
}

/// <summary>
/// Formatos aceptados (CA-HU-015-01 y 02): PDF, JPG y PNG. Se exigen la extensión y la firma del
/// contenido, para que un archivo renombrado no pase como PDF o imagen.
/// </summary>
public static class FormatosDocumento
{
    private static readonly Dictionary<string, (string Mime, byte[] Firma)> PorExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", "%PDF-"u8.ToArray()),
        [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    };

    /// <summary>Bytes iniciales que hay que leer para comprobar la firma.</summary>
    public const int BytesFirma = 8;

    /// <summary>Tipo MIME del archivo si la extensión y la firma corresponden a PDF, JPG o PNG; null si no.</summary>
    public static string? MimeDe(string nombreArchivo, ReadOnlySpan<byte> inicio)
    {
        var extension = Path.GetExtension(nombreArchivo);
        if (!PorExtension.TryGetValue(extension, out var formato) || !inicio.StartsWith(formato.Firma))
        {
            return null;
        }

        return formato.Mime;
    }

    /// <summary>Extensión normalizada para el archivo en disco (.pdf, .jpg o .png).</summary>
    public static string ExtensionEnDisco(string tipoMime) => tipoMime switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => throw new ArgumentOutOfRangeException(nameof(tipoMime), tipoMime, "Formato no permitido."),
    };
}
