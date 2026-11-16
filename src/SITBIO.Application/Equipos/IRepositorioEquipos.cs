using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>Equipos del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioEquipos
{
    /// <summary>Página de equipos que cumplen el filtro, ordenados por placa.</summary>
    Task<(IReadOnlyList<EquipoLeido> Items, int Total)> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken);

    /// <summary>
    /// Equipo con sus nombres y su plan activo; null si no existe o es de otro tenant (SEC-003).
    /// Con <paramref name="paraEditar"/>, el equipo y el plan quedan con seguimiento de cambios.
    /// </summary>
    Task<EquipoLeido?> ObtenerAsync(Guid id, bool paraEditar, CancellationToken cancellationToken);

    Task<TipoEquipo?> TipoEquipoAsync(Guid id, CancellationToken cancellationToken);

    Task<ElementoCatalogo?> UbicacionAsync(Guid id, CancellationToken cancellationToken);

    Task<ElementoCatalogo?> ProveedorAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>¿Otro equipo de la institución tiene la misma serie o la misma placa? (RN-001).</summary>
    Task<Duplicados> DuplicadosAsync(string numeroSerie, string placaInventario, Guid? exceptoEquipoId, CancellationToken cancellationToken);

    /// <summary>Plan del tenant del token, su máximo de equipos y cuántos están activos (RN-009).</summary>
    Task<CapacidadEquipos> CapacidadEquiposAsync(CancellationToken cancellationToken);

    void Agregar(EquipoBiomedico equipo);

    void Agregar(PlanPreventivo plan);

    /// <summary>Un cambio de estado del equipo (RF-014). El historial solo admite inserciones.</summary>
    void Agregar(HistorialEstadoEquipo registro);

    /// <summary>
    /// Historial de estados del equipo con los nombres de los estados y del usuario, del más
    /// reciente al más antiguo; vacío si el equipo es de otro tenant.
    /// </summary>
    Task<IReadOnlyList<CambioEstadoLeido>> HistorialAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>Guarda los cambios. Una violación de placa o serie única se responde como RN-001 (409).</summary>
    Task GuardarAsync(CancellationToken cancellationToken);
}
