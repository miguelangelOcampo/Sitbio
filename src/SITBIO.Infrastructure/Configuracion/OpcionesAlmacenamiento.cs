namespace Sitbio.Infrastructure.Configuracion;

/// <summary>Sección "Storage" de la configuración (EXT-003).</summary>
public sealed class OpcionesAlmacenamiento
{
    public const string Seccion = "Storage";

    public string AttachmentsPath { get; set; } = string.Empty;

    public int TamanoMaximoMb { get; set; } = 10;
}
