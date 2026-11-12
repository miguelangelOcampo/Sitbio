using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Tiquetes;

/// <summary>
/// Tiquete de API-TIQ (DOC-06). <see cref="PrioridadSugerida"/> es la prioridad que se propone para la
/// orden de trabajo (CA-HU-018-01); <see cref="EquipoFueraDeServicio"/> indica el estado actual del equipo
/// (RN-005).
/// </summary>
public sealed record TiqueteDto(
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
    bool EquipoFueraDeServicio)
{
    public static TiqueteDto Desde(TiqueteLeido leido)
    {
        ArgumentNullException.ThrowIfNull(leido);
        var t = leido.Tiquete;
        return new TiqueteDto(
            t.Id,
            t.Codigo,
            t.EquipoId,
            leido.PlacaInventario,
            leido.NumeroSerie,
            leido.TipoEquipo,
            leido.Ubicacion,
            t.TipoFalla,
            t.Descripcion,
            t.Criticidad,
            t.PrioridadSugerida,
            t.Estado,
            t.FechaReporte,
            t.FechaCierre,
            t.ReportanteId,
            leido.Reportante,
            leido.EstadoEquipo,
            leido.EstadoEquipo == GestionTiquetes.CodigoFueraDeServicio);
    }
}

/// <summary>Cuerpo de POST /tiquetes (UI-009). Quién reporta y la institución salen del token.</summary>
public sealed record DatosTiquete(Guid? EquipoId, string? TipoFalla, string? Descripcion, string? Criticidad);

/// <summary>Filtros de GET /tiquetes (bandeja UI-011): estado y criticidad, con paginación.</summary>
public sealed record FiltroTiquetes(string? Estado, string? Criticidad, int Pagina = 1, int Tamano = 20);

/// <summary>Tiquete con los datos del equipo y de quien lo reportó, para armar el DTO.</summary>
public sealed record TiqueteLeido(
    Tiquete Tiquete,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    string Reportante,
    string EstadoEquipo);
