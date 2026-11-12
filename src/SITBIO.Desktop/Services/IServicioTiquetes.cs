using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>API-TIQ (HU-003, HU-018): reporte de fallas y catálogo de tipos de falla para el formulario UI-009.</summary>
public interface IServicioTiquetes
{
    /// <summary>GET /tiquetes/tipos-falla: nombres de los tipos de falla activos de la institución.</summary>
    Task<IReadOnlyList<string>> TiposFallaAsync(CancellationToken cancellationToken);

    /// <summary>POST /tiquetes: crea el tiquete y devuelve su código TQT-######.</summary>
    Task<Tiquete> ReportarAsync(DatosTiquete datos, CancellationToken cancellationToken);
}

/// <summary>Valores de criticidad (script 01; RF-018).</summary>
public static class Criticidades
{
    public const string Critica = "critica";
    public const string Alta = "alta";
    public const string Media = "media";
    public const string Baja = "baja";
}

/// <summary>Cuerpo de POST /tiquetes. La descripción es opcional (DOC-05).</summary>
public sealed record DatosTiquete(Guid EquipoId, string TipoFalla, string? Descripcion, string Criticidad);

/// <summary>Tiquete tal como lo devuelve la API (TiqueteDto).</summary>
public sealed record Tiquete(
    Guid Id,
    string Codigo,
    Guid EquipoId,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    string TipoFalla,
    string Descripcion,
    string Criticidad,
    string PrioridadSugerida,
    string Estado,
    DateTimeOffset FechaReporte,
    DateTimeOffset? FechaCierre,
    Guid ReportanteId,
    string Reportante,
    string EstadoEquipo,
    bool EquipoFueraDeServicio);

public sealed class ServicioTiquetesHttp(ClienteApi cliente) : IServicioTiquetes
{
    public Task<IReadOnlyList<string>> TiposFallaAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<IReadOnlyList<string>>(cliente, HttpMethod.Get, "tiquetes/tipos-falla", null, Mensaje, cancellationToken);

    public Task<Tiquete> ReportarAsync(DatosTiquete datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Tiquete>(cliente, HttpMethod.Post, "tiquetes", datos, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite reportar fallas.",
        404 => "Ese equipo ya no está en el inventario de tu institución. Búscalo de nuevo.",
        >= 500 => "El servidor no pudo registrar el reporte. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
