using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Sitbio.Infrastructure.Configuracion;

/// <summary>Sección "Storage" de la configuración (EXT-003).</summary>
public sealed class OpcionesAlmacenamiento
{
    public const string Seccion = "Storage";

    public string AttachmentsPath { get; set; } = string.Empty;

    public int TamanoMaximoMb { get; set; } = 10;

    /// <summary>Lee la sección «Storage» (AttachmentsPath y TamanoMaximoMb, 10 MB por defecto).</summary>
    public static OpcionesAlmacenamiento Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var seccion = configuration.GetSection(Seccion);
        return new OpcionesAlmacenamiento
        {
            AttachmentsPath = seccion[nameof(AttachmentsPath)] ?? string.Empty,
            TamanoMaximoMb = int.TryParse(seccion[nameof(TamanoMaximoMb)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb) && mb > 0 ? mb : 10,
        };
    }
}
