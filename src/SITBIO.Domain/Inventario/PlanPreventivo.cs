using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Plan de mantenimiento preventivo de un equipo (tabla <c>plan_preventivo</c>, script 01 + V008).
/// <see cref="UltimaEjecucion"/> es el último preventivo conocido (CA-HU-002-06); si la próxima
/// fecha ya pasó, el plan nace vencido. SITBIO no asigna personal de la IPS al plan.
/// </summary>
public sealed class PlanPreventivo : EntidadTenant
{
    public const string Activo = "activo";
    public const string Suspendido = "suspendido";
    public const string Finalizado = "finalizado";

    // Constructor para EF Core.
    private PlanPreventivo(Guid tenantId)
        : base(tenantId)
    {
    }

    public PlanPreventivo(
        Guid tenantId,
        Guid equipoId,
        string descripcion,
        int periodicidadDias,
        DateOnly proximaFecha,
        DateTimeOffset fechaCreacion,
        DateOnly? ultimaEjecucion = null)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("El plan debe ser de un equipo.", nameof(equipoId));
        Descripcion = Texto.Requerido(descripcion, nameof(descripcion), 300);
        Estado = Activo;
        FechaCreacion = fechaCreacion;
        Programar(periodicidadDias, proximaFecha, ultimaEjecucion);
    }

    public Guid EquipoId { get; private set; }

    public string Descripcion { get; private set; } = string.Empty;

    public short PeriodicidadDias { get; private set; }

    public DateOnly ProximaFecha { get; private set; }

    public DateOnly? UltimaEjecucion { get; private set; }

    public string Estado { get; private set; } = Activo;

    public DateTimeOffset FechaCreacion { get; private set; }

    /// <summary>Periodicidad y fechas; el último preventivo debe ser anterior al próximo (CHECK de la V008).</summary>
    public void Programar(int periodicidadDias, DateOnly proximaFecha, DateOnly? ultimaEjecucion)
    {
        if (periodicidadDias is < 1 or > short.MaxValue)
        {
            throw new ArgumentException("La periodicidad del preventivo debe ser de al menos un día.", nameof(periodicidadDias));
        }

        if (ultimaEjecucion is { } ultima && ultima >= proximaFecha)
        {
            throw new ArgumentException("El último preventivo debe ser anterior al próximo.", nameof(ultimaEjecucion));
        }

        PeriodicidadDias = (short)periodicidadDias;
        ProximaFecha = proximaFecha;
        UltimaEjecucion = ultimaEjecucion;
    }

    /// <summary>Activo y con la próxima fecha ya pasada: genera alerta (RF-020).</summary>
    public bool EstaVencido(DateOnly hoy) => Estado == Activo && ProximaFecha < hoy;
}
