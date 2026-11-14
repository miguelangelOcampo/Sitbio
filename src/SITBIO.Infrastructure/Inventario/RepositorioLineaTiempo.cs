using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Equipos;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>Historia del equipo para la línea de tiempo (HU-023); el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioLineaTiempo(SitbioDbContext contexto) : IRepositorioLineaTiempo
{
    public async Task<HistoriaEquipo?> HistoriaAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        var equipo = await contexto.Equipos.AsNoTracking()
            .Where(e => e.Id == equipoId)
            .Select(e => new { e.Id, e.PlacaInventario, e.FechaCreacion })
            .SingleOrDefaultAsync(cancellationToken);
        if (equipo is null)
        {
            return null;
        }

        var estados = await (from h in contexto.HistorialEstados.AsNoTracking()
                             where h.EquipoId == equipoId
                             join nuevo in contexto.EstadosEquipo on h.NuevoEstadoId equals nuevo.Id
                             join anterior in contexto.EstadosEquipo on h.EstadoAnteriorId equals (short?)anterior.Id into anteriores
                             from anterior in anteriores.DefaultIfEmpty()
                             join u in contexto.Usuarios on h.UsuarioId equals u.Id
                             orderby h.FechaCambio
                             select new EstadoLeido(h, anterior == null ? null : anterior.Nombre, nuevo.Nombre, u.Nombres + " " + u.Apellidos))
            .ToListAsync(cancellationToken);

        var tiquetes = await contexto.Tiquetes.AsNoTracking()
            .Where(t => t.EquipoId == equipoId)
            .OrderBy(t => t.FechaReporte)
            .ToListAsync(cancellationToken);

        var ordenes = await (from o in contexto.Ordenes.AsNoTracking()
                             where o.EquipoId == equipoId
                             join tec in contexto.Usuarios on o.TecnicoId equals tec.Id
                             join a in contexto.Usuarios on o.AprobadoPorId equals (Guid?)a.Id into aprobadores
                             from a in aprobadores.DefaultIfEmpty()
                             join t in contexto.Tiquetes on o.TiqueteId equals (Guid?)t.Id into tiquetesOrden
                             from t in tiquetesOrden.DefaultIfEmpty()
                             orderby o.FechaCreacion
                             select new OrdenDelEquipo(o, tec.Nombres + " " + tec.Apellidos, a == null ? null : a.Nombres + " " + a.Apellidos, t == null ? null : t.Codigo))
            .ToListAsync(cancellationToken);

        var calibraciones = await (from c in contexto.Calibraciones.AsNoTracking()
                                   where c.EquipoId == equipoId
                                   join r in contexto.Usuarios on c.RegistradoPorId equals r.Id
                                   orderby c.FechaCalibracion
                                   select new CalibracionDelEquipo(c, r.Nombres + " " + r.Apellidos))
            .ToListAsync(cancellationToken);

        return new HistoriaEquipo(equipo.Id, equipo.PlacaInventario, equipo.FechaCreacion, estados, tiquetes, ordenes, calibraciones);
    }
}
