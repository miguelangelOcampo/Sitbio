using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Catalogos;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Catalogos;

/// <summary>Catálogos maestros sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioCatalogos(SitbioDbContext contexto) : IRepositorioCatalogos
{
    public async Task<IReadOnlyList<CatalogoDto>> TiposEquipoAsync(CancellationToken cancellationToken) =>
        await contexto.TiposEquipo.AsNoTracking()
            .OrderBy(t => t.Nombre).ThenBy(t => t.Id)
            .Select(t => new CatalogoDto(
                t.Id, t.Nombre, t.EstadoActivo, t.ClaseRiesgoDefecto, t.RequiereCalibracion, (int?)t.PeriodicidadCalibracionMeses))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogoDto>> UbicacionesAsync(CancellationToken cancellationToken) =>
        await contexto.Ubicaciones.AsNoTracking()
            .OrderBy(u => u.Nombre).ThenBy(u => u.Id)
            .Select(u => new CatalogoDto(u.Id, u.Nombre, u.EstadoActivo, null, null, null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogoDto>> ProveedoresAsync(CancellationToken cancellationToken) =>
        await contexto.Proveedores.AsNoTracking()
            .OrderBy(p => p.Nombre).ThenBy(p => p.Id)
            .Select(p => new CatalogoDto(p.Id, p.Nombre, p.EstadoActivo, null, null, null))
            .ToListAsync(cancellationToken);
}
