using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Indicadores;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Indicadores;

/// <summary>Datos del tablero (HU-026); el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioIndicadores(SitbioDbContext contexto) : IRepositorioIndicadores
{
    public async Task<DatosIndicadores> DatosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken)
    {
        var primerDia = DateOnly.FromDateTime(desde.ToOffset(TimeSpan.FromHours(-5)).DateTime);
        var ultimoDia = DateOnly.FromDateTime(hasta.ToOffset(TimeSpan.FromHours(-5)).DateTime);
        var ordenes = contexto.Ordenes.AsNoTracking();

        var preventivas = await ordenes
            .Where(o => o.TipoMantenimiento == TiposMantenimiento.Preventivo && o.FechaProgramada >= primerDia && o.FechaProgramada < ultimoDia)
            .GroupBy(_ => 1)
            .Select(g => new { Programadas = g.Count(), Cumplidas = g.Count(o => o.Estado == EstadosOrden.Cerrada || o.Estado == EstadosOrden.Aprobada) })
            .SingleOrDefaultAsync(cancellationToken);

        var reparaciones = await ordenes
            .Where(o => o.TipoMantenimiento == TiposMantenimiento.Correctivo && o.FechaCierre >= desde && o.FechaCierre < hasta)
            .Select(o => new { o.FechaCreacion, Cierre = o.FechaCierre!.Value })
            .ToListAsync(cancellationToken);

        var fallas = await contexto.Tiquetes.AsNoTracking().CountAsync(t => t.FechaReporte >= desde && t.FechaReporte < hasta, cancellationToken);

        var inventario = await contexto.Equipos.AsNoTracking()
            .Where(e => e.FechaBaja == null && e.EstadoEquipoId != EstadosEquipo.Baja)
            .GroupBy(_ => 1)
            .Select(g => new { Activos = g.Count(), Fuera = g.Count(e => e.EstadoEquipoId == EstadosEquipo.FueraDeServicio) })
            .SingleOrDefaultAsync(cancellationToken);

        return new DatosIndicadores(
            preventivas?.Programadas ?? 0,
            preventivas?.Cumplidas ?? 0,
            reparaciones.Select(r => r.Cierre - r.FechaCreacion).ToList(),
            fallas,
            inventario?.Activos ?? 0,
            inventario?.Fuera ?? 0);
    }

    public async Task<ParqueDto> ParqueAsync(CancellationToken cancellationToken)
    {
        var activos = contexto.Equipos.AsNoTracking().Where(e => e.FechaBaja == null && e.EstadoEquipoId != EstadosEquipo.Baja);
        var conteo = await activos
            .GroupBy(e => e.EstadoEquipoId)
            .Select(g => new { Estado = g.Key, Cantidad = g.Count() })
            .ToListAsync(cancellationToken);
        var fuera = await (
                from e in activos
                where e.EstadoEquipoId == EstadosEquipo.FueraDeServicio
                join t in contexto.TiposEquipo.AsNoTracking() on e.TipoEquipoId equals t.Id
                join u in contexto.Ubicaciones.AsNoTracking() on e.UbicacionId equals u.Id
                orderby e.PlacaInventario
                select new EquipoFueraDto(e.Id, e.PlacaInventario, t.Nombre, u.Nombre))
            .ToListAsync(cancellationToken);

        int Cantidad(short estado) => conteo.Where(c => c.Estado == estado).Sum(c => c.Cantidad);
        return new ParqueDto(Cantidad(EstadosEquipo.Operativo), Cantidad(EstadosEquipo.EnMantenimiento), Cantidad(EstadosEquipo.FueraDeServicio), fuera);
    }
}
