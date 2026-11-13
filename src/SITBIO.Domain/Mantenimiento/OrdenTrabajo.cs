using System.Text.RegularExpressions;
using Sitbio.Domain.Comun;
using Sitbio.Domain.Inventario;
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

/// <summary>Acciones del técnico sobre su orden (API-OT-ESTADO): iniciar y pausar (HU-005) y cerrar (HU-021).</summary>
public static class AccionesOrden
{
    public const string Iniciar = "iniciar";
    public const string Pausar = "pausar";
    public const string Cerrar = "cerrar";

    public static IReadOnlyList<string> Todas { get; } = [Iniciar, Pausar, Cerrar];
}

/// <summary>
/// Orden de trabajo (tabla <c>orden_trabajo</c>, script 01; HU-004, RF-007). La correctiva nace de un
/// tiquete «recibido» al asignarla a un Técnico de Mantenimiento activo (RN-003, lo valida la
/// aplicación); queda «asignada» y el tiquete pasa a «asignado». Un tiquete tiene a lo sumo una orden
/// (<c>uq_orden_tiquete</c>). La preventiva nace de un plan cuando llega su próxima fecha (HU-019); la
/// genera el sistema, sin quién la asigna.
/// </summary>
public sealed partial class OrdenTrabajo : EntidadTenant
{
    /// <summary>Longitud de <c>firma_aprobacion</c> (script 01).</summary>
    public const int LongitudMaximaFirma = 100;

    // Constructor para EF Core.
    private OrdenTrabajo(Guid tenantId)
        : base(tenantId)
    {
    }

    private OrdenTrabajo(
        Guid tenantId,
        Guid equipoId,
        Guid? tiqueteId,
        Guid? planId,
        Guid tecnicoId,
        Guid? asignadoPorId,
        string tipoMantenimiento,
        string prioridad,
        DateOnly? fechaProgramada,
        DateTimeOffset fechaAsignacion)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("La orden debe tener un equipo.", nameof(equipoId));
        TiqueteId = tiqueteId;
        PlanId = planId;
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

    /// <summary>Plan de origen (preventiva); null en una correctiva.</summary>
    public Guid? PlanId { get; private set; }

    /// <summary>Técnico de Mantenimiento (AC-03) que ejecuta la intervención.</summary>
    public Guid TecnicoId { get; private set; }

    /// <summary>Quién asignó la orden (sale del token); null en la preventiva que genera el sistema.</summary>
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

    /// <summary>HU-021: cuando el técnico la cerró; queda pendiente de la aprobación del Ingeniero.</summary>
    public DateTimeOffset? FechaCierre { get; private set; }

    /// <summary>Ingeniero Biomédico (AC-02) que la aprobó con su firma (HU-021).</summary>
    public Guid? AprobadoPorId { get; private set; }

    public DateTimeOffset? FechaAprobacion { get; private set; }

    /// <summary>Firma electrónica interna del Ingeniero (texto; <c>ck_orden_firma</c> exige la fecha de aprobación).</summary>
    public string? FirmaAprobacion { get; private set; }

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

    /// <summary>
    /// CA-HU-021-01: el técnico cierra su orden en ejecución o pausada cuando ya registró la intervención.
    /// Queda «cerrada» y pendiente de la aprobación del Ingeniero.
    /// </summary>
    public void Cerrar(DateTimeOffset ahora)
    {
        if (!AdmiteIntervencion)
        {
            throw new InvalidOperationException($"La orden está «{Estado}»; solo se cierra una orden en ejecución o pausada.");
        }

        if (IntervencionId is null)
        {
            throw new InvalidOperationException("Registra la intervención antes de cerrar la orden.");
        }

        Estado = EstadosOrden.Cerrada;
        FechaCierre = ahora;
    }

    /// <summary>
    /// CA-HU-021-02: el Ingeniero aprueba la orden cerrada con su firma. Desde aquí la orden y su
    /// intervención son inalterables (RN-004).
    /// </summary>
    public void Aprobar(Guid ingenieroId, string firma, DateTimeOffset ahora)
    {
        if (Estado != EstadosOrden.Cerrada)
        {
            throw new InvalidOperationException($"La orden está «{Estado}»; solo se aprueba una orden cerrada.");
        }

        // Se valida todo antes de cambiar la orden: si algo falla, queda como estaba.
        var firmaLimpia = Texto.Requerido(firma, nameof(firma), LongitudMaximaFirma);
        AprobadoPorId = ingenieroId != Guid.Empty ? ingenieroId : throw new ArgumentException("La aprobación debe tener quién la firma.", nameof(ingenieroId));
        FirmaAprobacion = firmaLimpia;
        Estado = EstadosOrden.Aprobada;
        FechaAprobacion = ahora;
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
            null,
            tecnicoId,
            asignadoPorId,
            TiposMantenimiento.Correctivo,
            prioridad ?? tiquete.PrioridadSugerida,
            fechaProgramada,
            ahora);
        var cambio = tiquete.Asignar(asignadoPorId, "Orden de trabajo asignada al técnico.", ahora);
        return (orden, cambio);
    }

    /// <summary>Prioridad de la orden preventiva generada.</summary>
    public const string PrioridadPreventiva = Criticidades.Media;

    /// <summary>
    /// CA-HU-019-02: llegó la próxima fecha del plan activo; el sistema genera la orden preventiva para
    /// el técnico elegido, programada para esa fecha.
    /// </summary>
    public static OrdenTrabajo Preventiva(PlanPreventivo plan, Guid tecnicoId, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Estado != PlanPreventivo.Activo)
        {
            throw new InvalidOperationException("Solo un plan activo genera órdenes preventivas.");
        }

        return new OrdenTrabajo(
            plan.TenantId,
            plan.EquipoId,
            null,
            plan.Id,
            tecnicoId,
            null,
            TiposMantenimiento.Preventivo,
            PrioridadPreventiva,
            plan.ProximaFecha,
            ahora);
    }

    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex PatronObjectId();
}
