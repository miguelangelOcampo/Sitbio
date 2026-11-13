using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Ordenes;

/// <summary>Órdenes de trabajo del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioOrdenes
{
    /// <summary>
    /// Página de órdenes de la más urgente a la menos urgente (crítica, alta, media, baja) y, dentro de cada
    /// prioridad, de la más reciente a la más antigua. Con <paramref name="tecnicoId"/>, solo las de ese técnico.
    /// </summary>
    Task<(IReadOnlyList<OrdenLeida> Items, int Total)> ListarAsync(FiltroOrdenes filtro, Guid? tecnicoId, CancellationToken cancellationToken);

    /// <summary>Orden por id; null si no existe, es de otro tenant o (con <paramref name="tecnicoId"/>) es de otro técnico.</summary>
    Task<OrdenLeida?> ObtenerAsync(Guid id, Guid? tecnicoId, CancellationToken cancellationToken);

    /// <summary>Tiquete con seguimiento de cambios (la asignación cambia su estado); null si no existe o es de otro tenant.</summary>
    Task<Tiquete?> TiqueteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Usuario de la institución con su rol y si está activo; null si no existe o es de otro tenant.</summary>
    Task<TecnicoCandidato?> TecnicoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// En una sola transacción: inserta la orden, guarda el nuevo estado del tiquete y su historial. Si otro
    /// ingeniero ya creó la orden de ese tiquete (<c>uq_orden_tiquete</c>), responde conflicto.
    /// </summary>
    Task RegistrarAsync(OrdenTrabajo orden, HistorialTiquete cambioTiquete, CancellationToken cancellationToken);
}
