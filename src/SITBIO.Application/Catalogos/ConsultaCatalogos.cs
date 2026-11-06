using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Catalogos;

/// <summary>
/// Consulta de API-CAT (GET /catalogos/{tipo}): tipos de equipo, ubicaciones y proveedores para los
/// formularios (UI-007). Un tipo de catálogo desconocido responde 404.
/// </summary>
public sealed class ConsultaCatalogos(IRepositorioCatalogos repositorio)
{
    public Task<IReadOnlyList<CatalogoDto>> ListarAsync(string tipo, CancellationToken cancellationToken) =>
        tipo.Trim().ToLowerInvariant() switch
        {
            TiposCatalogo.TiposEquipo => repositorio.TiposEquipoAsync(cancellationToken),
            TiposCatalogo.Ubicaciones => repositorio.UbicacionesAsync(cancellationToken),
            TiposCatalogo.Proveedores => repositorio.ProveedoresAsync(cancellationToken),
            _ => throw new ErrorNoEncontrado("catálogo"),
        };
}
