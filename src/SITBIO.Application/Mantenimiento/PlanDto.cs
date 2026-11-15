using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Mantenimiento;

/// <summary>Plan preventivo de API-PLAN (DOC-06) con los datos del equipo; <see cref="Vencido"/> si la próxima fecha ya pasó.</summary>
public sealed record PlanDto(
    Guid Id,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string Descripcion,
    int PeriodicidadDias,
    DateOnly? UltimaEjecucion,
    DateOnly ProximaFecha,
    string Estado,
    bool Vencido,
    DateTimeOffset FechaCreacion)
{
    public static PlanDto Desde(PlanLeido leido, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(leido);
        var p = leido.Plan;
        return new PlanDto(
            p.Id,
            p.EquipoId,
            leido.PlacaInventario,
            leido.TipoEquipo,
            leido.Ubicacion,
            p.Descripcion,
            p.PeriodicidadDias,
            p.UltimaEjecucion,
            p.ProximaFecha,
            p.Estado,
            p.EstaVencido(hoy),
            p.FechaCreacion);
    }
}

/// <summary>
/// Cuerpo de POST /planes-preventivos. Con el último preventivo, el próximo se calcula (último +
/// periodicidad) salvo que se indique; sin él, hay que indicar el próximo.
/// </summary>
public sealed record DatosPlan(Guid? EquipoId, string? Descripcion, int? PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly? ProximaFecha);

/// <summary>Cuerpo de PUT /planes-preventivos/{id}: lo que no se envía no cambia. El estado suspende, reactiva o finaliza.</summary>
public sealed record DatosActualizacionPlan(string? Descripcion, int? PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly? ProximaFecha, string? Estado);

/// <summary>Filtros de GET /planes-preventivos.</summary>
public sealed record FiltroPlanes(Guid? EquipoId = null, string? Estado = null, int Pagina = 1, int Tamano = 20);

/// <summary>Plan con la placa, el tipo y la ubicación de su equipo.</summary>
public sealed record PlanLeido(PlanPreventivo Plan, string PlacaInventario, string TipoEquipo, string Ubicacion);

/// <summary>Equipo al que se asocia un plan: su tenant lo limita la persistencia (RN-008).</summary>
public sealed record EquipoDelPlan(Guid Id, string PlacaInventario, bool DadoDeBaja);
