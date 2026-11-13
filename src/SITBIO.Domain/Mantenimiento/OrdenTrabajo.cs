using System.Text.RegularExpressions;
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

/// <summary>Acciones del técnico sobre su orden (API-OT-ESTADO, HU-005). El cierre llega con HU-021.</summary>
public static class AccionesOrden
{
    public const string Iniciar = "iniciar";
    public const string Pausar = "pausar";

    public static IReadOnlyList<string> Todas { get; } = [Iniciar, Pausar];
}

/// <summary>
/// Orden de trabajo (tabla <c>orden_trabajo</c>, script 01; HU-004, RF-007). La correctiva nace de un
/// tiquete «recibido» al asignarla a un Técnico de Mantenimiento activo (RN-003, lo valida la
/// aplicación); queda «asignada» y el tiquete pasa a «asignado». Un tiquete tiene a lo sumo una orden
/// (<c>uq_orden_tiquete</c>).
/// </summary>
public sealed partial class OrdenTrabajo : EntidadTenant
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

    /// <summary>Primera vez que el técnico la inició (HU-005); no cambia al reanudarla.</summary>
    public DateTimeOffset? FechaInicio { get; private set; }

    /// <summary><c>_id</c> (ObjectId) del documento de <c>intervenciones</c> en MongoDB (RT-002).</summary>
    public string? IntervencionId { get; private set; }

    /// <summary>El técnico registra o corrige la intervención mientras la orden está en ejecución o pausada.</summary>
    public bool AdmiteIntervencion => Estado is EstadosOrden.EnEjecucion or EstadosOrden.Pausada;

    /// <summary>
    /// HU-005: «asignada» o «pausada» pasan a «en ejecución». Devuelve true la primera vez, cuando también
    /// fija la fecha de inicio (y el tiquete de origen debe pasar a «en ejecución»).
    /// </summary>
    public bool Iniciar(DateTimeOffset ahora)
    {
        if (Estado is not (EstadosOrden.Asignada or EstadosOrden.Pausada))
        {
            throw new InvalidOperationException($"La orden está «{Estado}»; solo se inicia una orden asignada o pausada.");
        }

        Estado = EstadosOrden.EnEjecucion;
        if (FechaInicio is not null)
        {
            return false;
        }

        FechaInicio = ahora;
        return true;
    }

    /// <summary>HU-005: el técnico pausa la orden en ejecución (por ejemplo, a la espera de un repuesto).</summary>
    public void Pausar()
    {
        if (Estado != EstadosOrden.EnEjecucion)
        {
            throw new InvalidOperationException($"La orden está «{Estado}»; solo se pausa una orden en ejecución.");
        }

        Estado = EstadosOrden.Pausada;
    }

    /// <summary>Guarda el enlace con el documento de la intervención (ObjectId de 24 caracteres, CHECK del script 01).</summary>
    public void EnlazarIntervencion(string objectId)
    {
        if (!PatronObjectId().IsMatch(objectId ?? string.Empty))
        {
            throw new ArgumentException("El enlace con la intervención debe ser un ObjectId de 24 caracteres hexadecimales.", nameof(objectId));
        }

        IntervencionId = objectId;
    }

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

    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex PatronObjectId();
}
