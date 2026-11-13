using Sitbio.Domain.Mantenimiento;

namespace Sitbio.Application.Ordenes;

/// <summary>Orden de trabajo de API-OT (DOC-06) con el equipo, el técnico y el tiquete de origen.</summary>
public sealed record OrdenDto(
    Guid Id,
    Guid EquipoId,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    Guid? TiqueteId,
    string? CodigoTiquete,
    string? TipoFalla,
    Guid TecnicoId,
    string Tecnico,
    Guid? AsignadoPorId,
    string TipoMantenimiento,
    string Prioridad,
    string Estado,
    DateOnly? FechaProgramada,
    DateTimeOffset? FechaAsignacion,
    DateTimeOffset FechaCreacion,
    DateTimeOffset? FechaInicio = null,
    bool TieneIntervencion = false)
{
    public static OrdenDto Desde(OrdenLeida leida)
    {
        ArgumentNullException.ThrowIfNull(leida);
        var o = leida.Orden;
        return new OrdenDto(
            o.Id,
            o.EquipoId,
            leida.PlacaInventario,
            leida.NumeroSerie,
            leida.TipoEquipo,
            leida.Ubicacion,
            o.TiqueteId,
            leida.CodigoTiquete,
            leida.TipoFalla,
            o.TecnicoId,
            leida.Tecnico,
            o.AsignadoPorId,
            o.TipoMantenimiento,
            o.Prioridad,
            o.Estado,
            o.FechaProgramada,
            o.FechaAsignacion,
            o.FechaCreacion,
            o.FechaInicio,
            o.IntervencionId is not null);
    }
}

/// <summary>
/// Cuerpo de POST /ordenes (UI-011): el tiquete, el técnico y, opcionales, la prioridad (por defecto la
/// sugerida por la criticidad) y la fecha programada. Quién asigna y la institución salen del token.
/// </summary>
public sealed record DatosOrden(Guid? TiqueteId, Guid? TecnicoId, string? Prioridad, DateOnly? FechaProgramada);

/// <summary>Filtros de GET /ordenes: estado y «asignadas=me» (las del técnico que consulta), con paginación.</summary>
public sealed record FiltroOrdenes(string? Estado, bool AsignadasAMi = false, int Pagina = 1, int Tamano = 20);

/// <summary>Orden con los datos del equipo, del técnico y del tiquete de origen, para armar el DTO.</summary>
public sealed record OrdenLeida(
    OrdenTrabajo Orden,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    string Tecnico,
    string? CodigoTiquete,
    string? TipoFalla);

/// <summary>Usuario candidato a ejecutar la orden, con su rol y si está activo (RN-003).</summary>
public sealed record TecnicoCandidato(Guid Id, string CodigoRol, bool Activo);

/// <summary>Técnico activo que puede recibir la orden, con cuántas órdenes abiertas tiene (panel de UI-011).</summary>
public sealed record TecnicoConCarga(Guid Id, string Nombre, int OrdenesAbiertas);
