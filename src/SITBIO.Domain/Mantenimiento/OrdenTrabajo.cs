using Sitbio.Domain.Comun;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Domain.Mantenimiento;

/// <summary>Tipos de mantenimiento de la orden (CHECK de <c>orden_trabajo</c>, script 01).</summary>
public static class TiposMantenimiento
{
    public const string Preventivo = "preventivo";
    public const string Correctivo = "correctivo";
}

/// <summary>Estados de la orden de trabajo (CHECK de <c>orden_trabajo</c>, script 01).</summary>
public static class EstadosOrden
{
    public const string Asignada = "asignada";
    public const string EnEjecucion = "en_ejecucion";
    public const string Pausada = "pausada";
    public const string Cerrada = "cerrada";
    public const string Aprobada = "aprobada";

    public static IReadOnlyList<string> Todos { get; } = [Asignada, EnEjecucion, Pausada, Cerrada, Aprobada];

    /// <summary>Estados en los que la orden todavía ocupa al técnico (su carga en UI-011).</summary>
    public static IReadOnlyList<string> Abiertos { get; } = [Asignada, EnEjecucion, Pausada];
}

/// <summary>
/// Orden de trabajo (tabla <c>orden_trabajo</c>, script 01; HU-004, RF-007). La correctiva nace de un
/// tiquete «recibido» al asignarla a un Técnico de Mantenimiento activo (RN-003, lo valida la
/// aplicación); queda «asignada» y el tiquete pasa a «asignado». Un tiquete tiene a lo sumo una orden
/// (<c>uq_orden_tiquete</c>).
/// </summary>
public sealed class OrdenTrabajo : EntidadTenant
{
    // Constructor para EF Core.
    private OrdenTrabajo(Guid tenantId)
        : base(tenantId)
    {
    }

    private OrdenTrabajo(
        Guid tenantId,
        Guid equipoId,
        Guid? tiqueteId,
        Guid tecnicoId,
        Guid asignadoPorId,
        string tipoMantenimiento,
        string prioridad,
        DateOnly? fechaProgramada,
        DateTimeOffset fechaAsignacion)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("La orden debe tener un equipo.", nameof(equipoId));
        TiqueteId = tiqueteId;
        TecnicoId = tecnicoId != Guid.Empty ? tecnicoId : throw new ArgumentException("Selecciona el técnico que ejecutará la orden.", nameof(tecnicoId));
        AsignadoPorId = asignadoPorId != Guid.Empty ? asignadoPorId : throw new ArgumentException("La orden debe tener quién la asigna.", nameof(asignadoPorId));
        TipoMantenimiento = tipoMantenimiento;
        Prioridad = Criticidades.EsValida(prioridad)
            ? prioridad
            : throw new ArgumentException("La prioridad debe ser crítica, alta, media o baja.", nameof(prioridad));
        Estado = EstadosOrden.Asignada;
        FechaProgramada = fechaProgramada;
        FechaAsignacion = fechaAsignacion;
        FechaCreacion = fechaAsignacion;
    }

    public Guid EquipoId { get; private set; }

    /// <summary>Tiquete de origen (correctiva); null en una preventiva.</summary>
    public Guid? TiqueteId { get; private set; }

    /// <summary>Técnico de Mantenimiento (AC-03) que ejecuta la intervención.</summary>
    public Guid TecnicoId { get; private set; }

    /// <summary>Quién asignó la orden (sale del token).</summary>
    public Guid? AsignadoPorId { get; private set; }

    public string TipoMantenimiento { get; private set; } = string.Empty;

    /// <summary>Por defecto, la prioridad sugerida por la criticidad del tiquete (CA-HU-018-01).</summary>
    public string Prioridad { get; private set; } = string.Empty;

    public string Estado { get; private set; } = string.Empty;

    public DateOnly? FechaProgramada { get; private set; }

    public DateTimeOffset? FechaAsignacion { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    /// <summary>
    /// CA-HU-004-01: crea la orden correctiva del tiquete y lo deja «asignado». Devuelve el cambio para
    /// <c>historial_tiquete</c>. Sin <paramref name="prioridad"/>, usa la sugerida por la criticidad.
    /// </summary>
    public static (OrdenTrabajo Orden, HistorialTiquete Cambio) Correctiva(
        Tiquete tiquete,
        Guid tecnicoId,
        Guid asignadoPorId,
        string? prioridad,
        DateOnly? fechaProgramada,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(tiquete);
        var orden = new OrdenTrabajo(
            tiquete.TenantId,
            tiquete.EquipoId,
            tiquete.Id,
            tecnicoId,
            asignadoPorId,
            TiposMantenimiento.Correctivo,
            prioridad ?? tiquete.PrioridadSugerida,
            fechaProgramada,
            ahora);
        var cambio = tiquete.Asignar(asignadoPorId, "Orden de trabajo asignada al técnico.", ahora);
        return (orden, cambio);
    }
}
