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
    /// Técnicos de Mantenimiento activos de la institución (RN-003) con sus órdenes abiertas (asignada, en
    /// ejecución o pausada), del menos al más ocupado y luego por nombre.
    /// </summary>
    Task<IReadOnlyList<TecnicoConCarga>> TecnicosAsync(CancellationToken cancellationToken);

    /// <summary>
    /// En una sola transacción: inserta la orden, guarda el nuevo estado del tiquete y su historial. Si otro
    /// ingeniero ya creó la orden de ese tiquete (<c>uq_orden_tiquete</c>), responde conflicto.
    /// </summary>
    Task RegistrarAsync(OrdenTrabajo orden, HistorialTiquete cambioTiquete, CancellationToken cancellationToken);

    /// <summary>Orden con seguimiento de cambios (HU-005 cambia su estado y su enlace); null si no existe o es de otro tenant.</summary>
    Task<OrdenTrabajo?> OrdenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tipo de equipo del equipo (para la versión activa de los campos dinámicos); null si no existe.</summary>
    Task<Guid?> TipoEquipoAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda en una transacción los cambios de las entidades con seguimiento (la orden y su tiquete) y,
    /// si hay, el nuevo registro de <c>historial_tiquete</c>.
    /// </summary>
    Task GuardarAsync(HistorialTiquete? cambioTiquete, CancellationToken cancellationToken);
}
