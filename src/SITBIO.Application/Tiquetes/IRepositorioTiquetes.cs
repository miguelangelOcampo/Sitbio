using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Tiquetes;

/// <summary>Tiquetes del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioTiquetes
{
    /// <summary>Página de tiquetes del filtro, del más reciente al más antiguo. Con <paramref name="reportanteId"/>, solo los de esa persona.</summary>
    Task<(IReadOnlyList<TiqueteLeido> Items, int Total)> ListarAsync(FiltroTiquetes filtro, Guid? reportanteId, CancellationToken cancellationToken);

    /// <summary>Tiquete por código; null si no existe, es de otro tenant o (con <paramref name="reportanteId"/>) lo reportó otra persona.</summary>
    Task<TiqueteLeido?> ObtenerAsync(string codigo, Guid? reportanteId, CancellationToken cancellationToken);

    /// <summary>Cambios de estado del tiquete, del más antiguo al más reciente (CA-HU-017-01).</summary>
    Task<IReadOnlyList<HistorialTiqueteLeido>> HistorialAsync(Guid tiqueteId, CancellationToken cancellationToken);

    /// <summary>Equipo con seguimiento de cambios (RN-005 puede cambiar su estado); null si no existe o es de otro tenant.</summary>
    Task<EquipoBiomedico?> EquipoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Nombre del tipo de falla activo del catálogo que coincide sin distinguir mayúsculas; null si no hay.</summary>
    Task<string?> TipoFallaActivoAsync(string nombre, CancellationToken cancellationToken);

    /// <summary>Nombres de los tipos de falla activos del catálogo, en orden alfabético.</summary>
    Task<IReadOnlyList<string>> TiposFallaActivosAsync(CancellationToken cancellationToken);

    /// <summary>
    /// En una sola transacción: numera el tiquete con el siguiente TQT-###### de la institución, lo
    /// inserta con su historial y, si hay <paramref name="cambioEquipo"/>, guarda el nuevo estado del
    /// equipo y su historial (RN-005, EVT-TIQUETE-CRITICO).
    /// </summary>
    Task RegistrarAsync(Tiquete tiquete, HistorialTiquete recepcion, HistorialEstadoEquipo? cambioEquipo, CancellationToken cancellationToken);
}
