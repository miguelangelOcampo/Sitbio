using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;

namespace Sitbio.Application.Mantenimiento;

/// <summary>Planes preventivos y alertas del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioPlanes
{
    /// <summary>Página de planes por próxima fecha (los más cercanos primero).</summary>
    Task<(IReadOnlyList<PlanLeido> Items, int Total)> ListarAsync(FiltroPlanes filtro, CancellationToken cancellationToken);

    /// <summary>Plan con los datos de su equipo; null si no existe o es de otro tenant.</summary>
    Task<PlanLeido?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Plan con seguimiento de cambios; null si no existe o es de otro tenant.</summary>
    Task<PlanPreventivo?> PlanAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Equipo de la institución; null si no existe o es de otro tenant.</summary>
    Task<EquipoDelPlan?> EquipoAsync(Guid equipoId, CancellationToken cancellationToken);

    void Agregar(PlanPreventivo plan);

    Task GuardarAsync(CancellationToken cancellationToken);

    /// <summary>Días de anticipación de las alertas de la institución (<c>tenant.dias_anticipacion_alerta</c>, HU-024).</summary>
    Task<int> DiasAnticipacionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// CA-HU-019-02: en una transacción con un bloqueo por institución (dos consultas simultáneas no
    /// generan la misma orden), pasa a <paramref name="generar"/> los planes activos de equipos no dados de
    /// baja con la próxima fecha hasta <paramref name="hoy"/> que aún no tienen orden para esa fecha, y los
    /// técnicos activos con su carga; guarda las órdenes que devuelva.
    /// </summary>
    Task<IReadOnlyList<OrdenTrabajo>> GenerarOrdenesAsync(
        DateOnly hoy,
        Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>> generar,
        CancellationToken cancellationToken);

    /// <summary>Planes activos de equipos no dados de baja con la próxima fecha hasta <paramref name="limite"/>, con la orden de esa fecha si existe.</summary>
    Task<IReadOnlyList<PlanPorVencer>> PlanesPorVencerAsync(DateOnly limite, CancellationToken cancellationToken);

    /// <summary>Equipos activos que requieren calibración (RN-010) cuya última vigencia vence hasta <paramref name="limite"/>.</summary>
    Task<IReadOnlyList<CalibracionPorVencer>> CalibracionesPorVencerAsync(DateOnly limite, CancellationToken cancellationToken);
}
