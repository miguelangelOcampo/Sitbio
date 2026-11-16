using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Plan de mantenimiento preventivo de un equipo (tabla <c>plan_preventivo</c>, script 01 + V008; HU-019).
/// <see cref="UltimaEjecucion"/> es el último preventivo conocido (CA-HU-002-06); si la próxima
/// fecha ya pasó, el plan nace vencido. Cuando llega la próxima fecha se genera la orden preventiva y,
/// al aprobarla, el plan avanza: último = la fecha de la aprobación y próximo = último + periodicidad.
/// Un plan «suspendido» no genera órdenes ni alertas hasta reactivarlo; uno «finalizado» ya no cambia.
/// </summary>
public sealed class PlanPreventivo : EntidadTenant
{
    public const string Activo = "activo";
    public const string Suspendido = "suspendido";
    public const string Finalizado = "finalizado";

    public const int LongitudMaximaDescripcion = 300;

    public static IReadOnlyList<string> Estados { get; } = [Activo, Suspendido, Finalizado];

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
        Descripcion = Texto.Requerido(descripcion, nameof(descripcion), LongitudMaximaDescripcion);
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

    /// <summary>Activo y con la próxima fecha hoy o antes: le corresponde su orden preventiva (CA-HU-019-02).</summary>
    public bool DebeGenerarOrden(DateOnly hoy) => Estado == Activo && ProximaFecha <= hoy;

    public void CambiarDescripcion(string descripcion)
    {
        ExigirNoFinalizado();
        Descripcion = Texto.Requerido(descripcion, nameof(descripcion), LongitudMaximaDescripcion);
    }

    /// <summary>Suspender, reactivar o finalizar. Un plan finalizado no vuelve a activarse.</summary>
    public void CambiarEstado(string estado)
    {
        if (!Estados.Contains(estado))
        {
            throw new ArgumentException("El estado del plan debe ser activo, suspendido o finalizado.", nameof(estado));
        }

        if (estado != Estado)
        {
            ExigirNoFinalizado();
        }

        Estado = estado;
    }

    /// <summary>
    /// La orden preventiva se aprobó en <paramref name="fecha"/>: queda como el último preventivo y el
    /// próximo pasa a fecha + periodicidad. Si el plan ya no está activo, no cambia.
    /// </summary>
    public void RegistrarEjecucion(DateOnly fecha)
    {
        if (Estado != Activo || (UltimaEjecucion is { } ultima && fecha <= ultima))
        {
            return;
        }

        UltimaEjecucion = fecha;
        ProximaFecha = fecha.AddDays(PeriodicidadDias);
    }

    private void ExigirNoFinalizado()
    {
        if (Estado == Finalizado)
        {
            throw new InvalidOperationException("El plan está finalizado; ya no se puede modificar.");
        }
    }
}
