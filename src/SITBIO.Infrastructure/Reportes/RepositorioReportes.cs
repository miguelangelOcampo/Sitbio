using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Reportes;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Reportes;

/// <summary>Historial de órdenes para los reportes (HU-007); el filtro global limita al tenant del token (RN-008).</summary>
public sealed class RepositorioReportes(SitbioDbContext contexto) : IRepositorioReportes
{
    public async Task<IReadOnlyList<FilaHistorialOrden>> HistorialOrdenesAsync(DateTimeOffset desde, DateTimeOffset hasta, int maximo, CancellationToken cancellationToken)
    {
        var filas = await (from o in contexto.Ordenes.AsNoTracking()
                           where o.FechaCreacion >= desde && o.FechaCreacion < hasta
                           join e in contexto.Equipos on o.EquipoId equals e.Id
                           join tipo in contexto.TiposEquipo on e.TipoEquipoId equals tipo.Id
                           join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                           join tec in contexto.Usuarios on o.TecnicoId equals tec.Id
                           join a in contexto.Usuarios on o.AprobadoPorId equals (Guid?)a.Id into aprobadores
                           from a in aprobadores.DefaultIfEmpty()
                           join t in contexto.Tiquetes on o.TiqueteId equals (Guid?)t.Id into tiquetes
                           from t in tiquetes.DefaultIfEmpty()
                           orderby o.FechaCreacion
                           select new
                           {
                               o.FechaCreacion,
                               e.PlacaInventario,
                               TipoEquipo = tipo.Nombre,
                               Ubicacion = u.Nombre,
                               o.TipoMantenimiento,
                               Tiquete = t == null ? null : t.Codigo,
                               Tecnico = tec.Nombres + " " + tec.Apellidos,
                               o.Estado,
                               o.FechaCierre,
                               Aprobador = a == null ? null : a.Nombres + " " + a.Apellidos,
                           })
            .Take(maximo)
            .ToListAsync(cancellationToken);

        return filas.Select(f => new FilaHistorialOrden(
            f.FechaCreacion,
            f.PlacaInventario,
            f.TipoEquipo,
            f.Ubicacion,
            f.TipoMantenimiento == TiposMantenimiento.Preventivo ? "Preventivo" : "Correctivo",
            f.Tiquete,
            f.Tecnico,
            f.Estado,
            f.FechaCierre,
            f.Aprobador)).ToList();
    }
}
