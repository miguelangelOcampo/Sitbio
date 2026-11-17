using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Busqueda;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Busqueda;

/// <summary>Consultas de la búsqueda global; el filtro global las limita al tenant del token (RN-008).</summary>
public sealed class RepositorioBusqueda(SitbioDbContext contexto) : IRepositorioBusqueda
{
    public async Task<IReadOnlyList<EquipoEncontradoDto>> EquiposAsync(string texto, int maximo, CancellationToken cancellationToken)
    {
        var buscado = texto.ToUpperInvariant();
        return await (from e in contexto.Equipos.AsNoTracking()
                      join tipo in contexto.TiposEquipo on e.TipoEquipoId equals tipo.Id
                      join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                      where e.PlacaInventario.ToUpper().Contains(buscado) || e.NumeroSerie.ToUpper().Contains(buscado)
                      orderby e.PlacaInventario
                      select new EquipoEncontradoDto(e.Id, e.PlacaInventario, e.NumeroSerie, tipo.Nombre, u.Nombre))
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TiqueteEncontradoDto>> TiquetesAsync(string texto, Guid? reportanteId, int maximo, CancellationToken cancellationToken)
    {
        var buscado = texto.ToUpperInvariant();
        var tiquetes = contexto.Tiquetes.AsNoTracking().Where(t => t.Codigo.ToUpper().Contains(buscado));
        if (reportanteId is { } reportante)
        {
            tiquetes = tiquetes.Where(t => t.ReportanteId == reportante);
        }

        return await (from t in tiquetes
                      join e in contexto.Equipos on t.EquipoId equals e.Id
                      orderby t.FechaReporte descending, t.Codigo descending
                      select new TiqueteEncontradoDto(t.Codigo, t.TipoFalla, t.Estado, e.PlacaInventario))
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrdenEncontradaDto>> OrdenesAsync(string texto, Guid? tecnicoId, int maximo, CancellationToken cancellationToken)
    {
        var buscado = texto.ToUpperInvariant();
        var ordenes = contexto.Ordenes.AsNoTracking();
        if (tecnicoId is { } tecnico)
        {
            ordenes = ordenes.Where(o => o.TecnicoId == tecnico);
        }

        // La referencia de la orden es el código del tiquete de origen o, sin tiquete (preventiva), la placa del equipo.
        return await (from o in ordenes
                      join e in contexto.Equipos on o.EquipoId equals e.Id
                      join t in contexto.Tiquetes on o.TiqueteId equals t.Id into origen
                      from t in origen.DefaultIfEmpty()
                      let referencia = t != null ? t.Codigo : e.PlacaInventario
                      where referencia.ToUpper().Contains(buscado)
                      orderby o.FechaCreacion descending, o.Id
                      select new OrdenEncontradaDto(o.Id, referencia, o.TipoMantenimiento, o.Estado, e.PlacaInventario))
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }
}
