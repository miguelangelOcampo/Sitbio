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

/// <summary>Cuerpo de POST /tiquetes (UI-009). Quién reporta y la institución salen del token; la descripción es opcional.</summary>
public sealed record DatosTiquete(Guid? EquipoId, string? TipoFalla, string? Descripcion, string? Criticidad);

/// <summary>
/// Filtros de GET /tiquetes: estado y criticidad, con paginación (bandeja UI-011). <see cref="Mios"/>
/// limita la lista a los tiquetes que reportó quien consulta (seguimiento UI-010, HU-017).
/// </summary>
public sealed record FiltroTiquetes(string? Estado, string? Criticidad, int Pagina = 1, int Tamano = 20, bool Mios = false, bool PorCriticidad = false);

/// <summary>Un cambio de estado del tiquete (CA-HU-017-01). La recepción no tiene estado anterior.</summary>
public sealed record HistorialTiqueteDto(
    string? EstadoAnterior,
    string NuevoEstado,
    string? Comentario,
    DateTimeOffset FechaCambio,
    Guid UsuarioId,
    string Usuario)
{
    public static HistorialTiqueteDto Desde(HistorialTiqueteLeido leido)
    {
        ArgumentNullException.ThrowIfNull(leido);
        var h = leido.Cambio;
        return new HistorialTiqueteDto(h.EstadoAnterior, h.NuevoEstado, h.Comentario, h.FechaCambio, h.UsuarioId, leido.Usuario);
    }
}

/// <summary>Cambio del historial con el nombre de quien lo hizo.</summary>
public sealed record HistorialTiqueteLeido(HistorialTiquete Cambio, string Usuario);

/// <summary>Tiquete con los datos del equipo y de quien lo reportó, para armar el DTO.</summary>
public sealed record TiqueteLeido(
    Tiquete Tiquete,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    string Reportante,
    string EstadoEquipo);
