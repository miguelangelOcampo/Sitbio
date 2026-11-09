namespace Sitbio.Application.Catalogos;

/// <summary>
/// Catálogos maestros del tenant del token (el filtro lo aplica la persistencia, RN-008), ordenados
/// por nombre e incluidos los inactivos. Por ahora solo consulta; crear y editar llegan con HU-025.
/// </summary>
public interface IRepositorioCatalogos
{
    Task<IReadOnlyList<CatalogoDto>> TiposEquipoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogoDto>> UbicacionesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogoDto>> ProveedoresAsync(CancellationToken cancellationToken);
}
