using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Mantenimiento;

/// <summary>Planes preventivos y alertas sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioPlanes(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioPlanes
{
    /// <summary>Espacio de los bloqueos consultivos de la generación de órdenes preventivas (uno por tenant).</summary>
    private const int EspacioBloqueo = 19;

    public async Task<(IReadOnlyList<PlanLeido> Items, int Total)> ListarAsync(FiltroPlanes filtro, CancellationToken cancellationToken)
    {
        var planes = contexto.PlanesPreventivos.AsNoTracking();
        if (filtro.EquipoId is { } equipoId)
        {
            planes = planes.Where(p => p.EquipoId == equipoId);
        }

        if (filtro.Estado is { } estado)
        {
            planes = planes.Where(p => p.Estado == estado);
        }

        var total = await planes.CountAsync(cancellationToken);
        var pagina = planes
            .OrderBy(p => p.ProximaFecha)
            .ThenBy(p => p.FechaCreacion)
            .ThenBy(p => p.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano);
        return (await Leer(pagina).ToListAsync(cancellationToken), total);
    }

    public Task<PlanLeido?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        Leer(contexto.PlanesPreventivos.AsNoTracking().Where(p => p.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<PlanPreventivo?> PlanAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.PlanesPreventivos.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<EquipoDelPlan?> EquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.AsNoTracking()
            .Where(e => e.Id == equipoId)
            .Select(e => new EquipoDelPlan(e.Id, e.PlacaInventario, e.FechaBaja != null))
            .SingleOrDefaultAsync(cancellationToken);

    public void Agregar(PlanPreventivo plan) => contexto.PlanesPreventivos.Add(plan);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);

    public async Task<int> DiasAnticipacionAsync(CancellationToken cancellationToken)
    {
        var tenant = contextoUsuario.TenantId;
        return await contexto.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant)
            .Select(t => (int)t.DiasAnticipacionAlerta)
            .SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrdenTrabajo>> GenerarOrdenesAsync(
        DateOnly hoy,
        Func<IReadOnlyList<PlanPreventivo>, IReadOnlyList<TecnicoConCarga>, IReadOnlyList<OrdenTrabajo>> generar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(generar);
        await using var transaccion = await contexto.Database.BeginTransactionAsync(cancellationToken);

        // Una consulta de alertas a la vez por institución: la segunda espera y ya ve las órdenes de la primera.
        var clave = contextoUsuario.TenantId.GetHashCode();
        await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({EspacioBloqueo}, {clave})", cancellationToken);

        var pendientes = await (from p in contexto.PlanesPreventivos
                                join e in contexto.Equipos on p.EquipoId equals e.Id
                                where p.Estado == PlanPreventivo.Activo
                                      && p.ProximaFecha <= hoy
                                      && e.FechaBaja == null
                                      && !contexto.Ordenes.Any(o => o.PlanId == p.Id && o.FechaProgramada == p.ProximaFecha)
                                select p)
            .ToListAsync(cancellationToken);
        if (pendientes.Count == 0)
        {
            return [];
        }

        var abiertos = EstadosOrden.Abiertos.ToList();
        var tecnicos = await (from u in contexto.Usuarios.AsNoTracking()
                              join r in contexto.Roles on u.RolId equals r.Id
                              where r.Codigo == CodigosRol.TecnicoMantenimiento && u.EstadoActivo
                              let carga = contexto.Ordenes.Count(o => o.TecnicoId == u.Id && abiertos.Contains(o.Estado))
                              orderby carga, u.Nombres, u.Apellidos, u.Id
                              select new TecnicoConCarga(u.Id, u.Nombres + " " + u.Apellidos, carga))
            .ToListAsync(cancellationToken);

        var ordenes = generar(pendientes, tecnicos);
        contexto.Ordenes.AddRange(ordenes);
        await contexto.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
        return ordenes;
    }

    public async Task<IReadOnlyList<PlanPorVencer>> PlanesPorVencerAsync(DateOnly limite, CancellationToken cancellationToken)
    {
        var planes = from p in contexto.PlanesPreventivos.AsNoTracking()
                     join e in contexto.Equipos on p.EquipoId equals e.Id
                     where p.Estado == PlanPreventivo.Activo && p.ProximaFecha <= limite && e.FechaBaja == null
                     select p;
        var leidos = await Leer(planes).ToListAsync(cancellationToken);
        if (leidos.Count == 0)
        {
            return [];
        }

        // La orden de la próxima fecha de cada plan (si ya se generó).
        var ids = leidos.Select(l => l.Plan.Id).ToList();
        var ordenes = await contexto.Ordenes.AsNoTracking()
            .Where(o => o.PlanId != null && ids.Contains(o.PlanId.Value))
            .Select(o => new { PlanId = o.PlanId!.Value, o.FechaProgramada, o.Id, o.Estado })
            .ToListAsync(cancellationToken);
        return leidos
            .Select(l => ordenes.FirstOrDefault(o => o.PlanId == l.Plan.Id && o.FechaProgramada == l.Plan.ProximaFecha) is { } orden
                ? new PlanPorVencer(l, orden.Id, orden.Estado)
                : new PlanPorVencer(l, null, null))
            .ToList();
    }

    public async Task<IReadOnlyList<CalibracionPorVencer>> CalibracionesPorVencerAsync(DateOnly limite, CancellationToken cancellationToken)
    {
        // Vigencia de cada equipo: el vencimiento más lejano de sus calibraciones (HU-022).
        var vigencias = await contexto.Calibraciones.AsNoTracking()
            .GroupBy(c => c.EquipoId)
            .Select(g => new { EquipoId = g.Key, FechaVencimiento = g.Max(c => c.FechaVencimiento) })
            .Where(v => v.FechaVencimiento <= limite)
            .ToListAsync(cancellationToken);
        if (vigencias.Count == 0)
        {
            return [];
        }

        var ids = vigencias.Select(v => v.EquipoId).ToList();
        var equipos = await (from e in contexto.Equipos.AsNoTracking()
                             join t in contexto.TiposEquipo on e.TipoEquipoId equals t.Id
                             join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                             where ids.Contains(e.Id) && e.RequiereCalibracion && e.FechaBaja == null
                             select new { e.Id, e.PlacaInventario, Tipo = t.Nombre, Ubicacion = u.Nombre })
            .ToListAsync(cancellationToken);
        var vence = vigencias.ToDictionary(v => v.EquipoId, v => v.FechaVencimiento);
        return equipos.Select(e => new CalibracionPorVencer(e.Id, e.PlacaInventario, e.Tipo, e.Ubicacion, vence[e.Id])).ToList();
    }

    private IQueryable<PlanLeido> Leer(IQueryable<PlanPreventivo> planes) =>
        from p in planes
        join e in contexto.Equipos on p.EquipoId equals e.Id
        join t in contexto.TiposEquipo on e.TipoEquipoId equals t.Id
        join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
        select new PlanLeido(p, e.PlacaInventario, t.Nombre, u.Nombre);
}
