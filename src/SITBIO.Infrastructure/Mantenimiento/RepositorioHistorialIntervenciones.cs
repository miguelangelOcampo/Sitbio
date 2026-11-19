using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Ordenes;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Mantenimiento;

/// <summary>
/// Lado PostgreSQL del historial de intervenciones (HU-005): qué equipos cumplen el filtro y los nombres para las
/// filas. El filtro global limita todo al tenant del token (RN-008).
/// </summary>
public sealed class RepositorioHistorialIntervenciones(SitbioDbContext contexto) : IRepositorioHistorialIntervenciones
{
    private const string Escape = "\\";

    public async Task<IReadOnlyList<Guid>> EquiposAsync(string? texto, Guid? tipoEquipoId, CancellationToken cancellationToken)
    {
        var equipos = contexto.Equipos.AsNoTracking();
        if (tipoEquipoId is { } tipo)
        {
            equipos = equipos.Where(e => e.TipoEquipoId == tipo);
        }

        if (texto is not null)
        {
            var patron = "%" + texto.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_") + "%";
            equipos = equipos.Where(e =>
                EF.Functions.ILike(e.PlacaInventario, patron, Escape)
                || EF.Functions.ILike(e.NumeroSerie, patron, Escape)
                || EF.Functions.ILike(e.Marca + " " + e.Modelo, patron, Escape)
                || contexto.TiposEquipo.Any(t => t.Id == e.TipoEquipoId && EF.Functions.ILike(t.Nombre, patron, Escape)));
        }

        return await equipos.Select(e => e.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, EquipoDelHistorial>> DatosEquiposAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, EquipoDelHistorial>();
        }

        return await (from e in contexto.Equipos.AsNoTracking()
                      where ids.Contains(e.Id)
                      join t in contexto.TiposEquipo on e.TipoEquipoId equals t.Id
                      select new { e.Id, e.PlacaInventario, Tipo = t.Nombre })
            .ToDictionaryAsync(x => x.Id, x => new EquipoDelHistorial(x.PlacaInventario, x.Tipo), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await contexto.Usuarios.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombres + " " + u.Apellidos, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> TiquetesDeOrdenesAsync(IReadOnlyCollection<Guid> ordenIds, CancellationToken cancellationToken)
    {
        if (ordenIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await (from o in contexto.Ordenes.AsNoTracking()
                      where ordenIds.Contains(o.Id)
                      join t in contexto.Tiquetes on o.TiqueteId equals (Guid?)t.Id
                      select new { o.Id, t.Codigo })
            .ToDictionaryAsync(x => x.Id, x => x.Codigo, cancellationToken);
    }
}
