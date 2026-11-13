using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Tiquetes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Peticiones;

/// <summary>Tiquetes sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioTiquetes(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioTiquetes
{
    public async Task<(IReadOnlyList<TiqueteLeido> Items, int Total)> ListarAsync(FiltroTiquetes filtro, Guid? reportanteId, CancellationToken cancellationToken)
    {
        var tiquetes = Filtrar(reportanteId);
        if (filtro.Estado is { } estado)
        {
            tiquetes = tiquetes.Where(t => t.Estado == estado);
        }

        if (filtro.Criticidad is { } criticidad)
        {
            tiquetes = tiquetes.Where(t => t.Criticidad == criticidad);
        }

        var total = await tiquetes.CountAsync(cancellationToken);
        var pagina = tiquetes
            .OrderByDescending(t => t.FechaReporte).ThenByDescending(t => t.Codigo)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano);
        var items = await Leer(pagina).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<TiqueteLeido?> ObtenerAsync(string codigo, Guid? reportanteId, CancellationToken cancellationToken) =>
        Leer(Filtrar(reportanteId).Where(t => t.Codigo == codigo)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<HistorialTiqueteLeido>> HistorialAsync(Guid tiqueteId, CancellationToken cancellationToken) =>
        await (from h in contexto.HistorialTiquetes.AsNoTracking()
               join u in contexto.Usuarios on h.UsuarioId equals u.Id
               where h.TiqueteId == tiqueteId
               orderby h.FechaCambio, h.Id
               select new HistorialTiqueteLeido(h, u.Nombres + " " + u.Apellidos))
            .ToListAsync(cancellationToken);

    public Task<EquipoBiomedico?> EquipoAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Equipos.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<string?> TipoFallaActivoAsync(string nombre, CancellationToken cancellationToken)
    {
        // Misma comparación que uq_tipo_falla_tenant_nombre (lower(nombre)).
        var buscado = nombre.ToLowerInvariant();
        return contexto.TiposFalla
            .Where(f => f.EstadoActivo && f.Nombre.ToLower() == buscado)
            .Select(f => f.Nombre)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> TiposFallaActivosAsync(CancellationToken cancellationToken) =>
        await contexto.TiposFalla.AsNoTracking()
            .Where(f => f.EstadoActivo)
            .OrderBy(f => f.Nombre)
            .Select(f => f.Nombre)
            .ToListAsync(cancellationToken);

    public async Task RegistrarAsync(Tiquete tiquete, HistorialTiquete recepcion, HistorialEstadoEquipo? cambioEquipo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiquete);
        await using var transaccion = await contexto.Database.BeginTransactionAsync(cancellationToken);

        // Un candado por institución mientras dure la transacción: dos reportes simultáneos no toman el
        // mismo consecutivo, y los de otras instituciones no esperan.
        var llave = $"tiquete:{contextoUsuario.TenantId}";
        await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({llave})::bigint)", cancellationToken);

        var ultimo = await contexto.Tiquetes
            .Where(t => t.Codigo.StartsWith(Tiquete.Prefijo))
            .OrderByDescending(t => t.Codigo)
            .Select(t => t.Codigo)
            .FirstOrDefaultAsync(cancellationToken);
        tiquete.Numerar((Tiquete.Consecutivo(ultimo) ?? 0) + 1);

        contexto.Tiquetes.Add(tiquete);
        contexto.HistorialTiquetes.Add(recepcion);
        if (cambioEquipo is not null)
        {
            // El equipo viene de EquipoAsync con seguimiento: su nuevo estado se guarda en este mismo SaveChanges.
            contexto.HistorialEstados.Add(cambioEquipo);
        }

        await contexto.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
    }

    private IQueryable<Tiquete> Filtrar(Guid? reportanteId)
    {
        var tiquetes = contexto.Tiquetes.AsNoTracking();
        return reportanteId is { } id ? tiquetes.Where(t => t.ReportanteId == id) : tiquetes;
    }

    private IQueryable<TiqueteLeido> Leer(IQueryable<Tiquete> tiquetes) =>
        from t in tiquetes
        join e in contexto.Equipos on t.EquipoId equals e.Id
        join tipo in contexto.TiposEquipo on e.TipoEquipoId equals tipo.Id
        join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
        join s in contexto.EstadosEquipo on e.EstadoEquipoId equals s.Id
        join r in contexto.Usuarios on t.ReportanteId equals r.Id
        select new TiqueteLeido(t, e.PlacaInventario, e.NumeroSerie, tipo.Nombre, u.Nombre, r.Nombres + " " + r.Apellidos, s.Codigo);
}
