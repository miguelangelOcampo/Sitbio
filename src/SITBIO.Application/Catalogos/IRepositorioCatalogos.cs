using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Catalogos;

/// <summary>
/// Catálogos maestros del tenant del token (el filtro lo aplica la persistencia, RN-008): tipos de
/// equipo, ubicaciones, proveedores y tipos de falla (HU-025). Las listas van ordenadas por nombre e
/// incluyen los inactivos. Los elementos no se borran: se inactivan (CA-HU-025-02).
/// </summary>
public interface IRepositorioCatalogos
{
    Task<IReadOnlyList<CatalogoDto>> TiposEquipoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogoDto>> UbicacionesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogoDto>> ProveedoresAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogoDto>> TiposFallaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Elemento del catálogo <paramref name="tipo"/> (un valor de <see cref="TiposCatalogo"/>) con
    /// seguimiento de cambios para editarlo; null si no existe o es de otra institución (SEC-003).
    /// </summary>
    Task<IElementoCatalogo?> ObtenerAsync(string tipo, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// ¿Otro elemento del mismo catálogo tiene ese nombre, sin distinguir mayúsculas ni espacios a
    /// los lados? <paramref name="exceptoId"/> excluye el elemento que se está editando.
    /// </summary>
    Task<bool> NombreEnUsoAsync(string tipo, string nombre, Guid? exceptoId, CancellationToken cancellationToken);

    /// <summary>¿Otro proveedor de la institución tiene ese NIT? (uq_proveedor_tenant_nit).</summary>
    Task<bool> NitProveedorEnUsoAsync(string nit, Guid? exceptoId, CancellationToken cancellationToken);

    void Agregar(IElementoCatalogo elemento);

    /// <summary>
    /// Guarda los cambios. Si dos registros simultáneos pasan la consulta previa, el índice único
    /// decide y se responde <see cref="ErroresCatalogo.CodigoDuplicado"/> (409).
    /// </summary>
    Task GuardarAsync(CancellationToken cancellationToken);
}
