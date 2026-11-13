using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Persistencia.Configuraciones;

namespace Sitbio.Infrastructure.Mantenimiento;

/// <summary>Órdenes de trabajo sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioOrdenes(SitbioDbContext contexto) : IRepositorioOrdenes
{
    public async Task<(IReadOnlyList<OrdenLeida> Items, int Total)> ListarAsync(FiltroOrdenes filtro, Guid? tecnicoId, CancellationToken cancellationToken)
    {
        var ordenes = Filtrar(tecnicoId);
        if (filtro.Estado is { } estado)
        {
            ordenes = ordenes.Where(o => o.Estado == estado);
        }

        var total = await ordenes.CountAsync(cancellationToken);
        var pagina = ordenes
            .OrderBy(o => o.Prioridad == Criticidades.Critica ? 0 : o.Prioridad == Criticidades.Alta ? 1 : o.Prioridad == Criticidades.Media ? 2 : 3)
            .ThenByDescending(o => o.FechaCreacion)
            .ThenBy(o => o.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano);
        var items = await Leer(pagina).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<OrdenLeida?> ObtenerAsync(Guid id, Guid? tecnicoId, CancellationToken cancellationToken) =>
        Leer(Filtrar(tecnicoId).Where(o => o.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<Tiquete?> TiqueteAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Tiquetes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<TecnicoCandidato?> TecnicoAsync(Guid id, CancellationToken cancellationToken) =>
        (from u in contexto.Usuarios.AsNoTracking()
         join r in contexto.Roles on u.RolId equals r.Id
         where u.Id == id
         select new TecnicoCandidato(u.Id, r.Codigo, u.EstadoActivo))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task RegistrarAsync(OrdenTrabajo orden, HistorialTiquete cambioTiquete, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orden);

        // El tiquete viene de TiqueteAsync con seguimiento: su nuevo estado se guarda en este mismo SaveChanges,
        // que EF Core envuelve en una transacción junto con la orden y el historial.
        contexto.Ordenes.Add(orden);
        contexto.HistorialTiquetes.Add(cambioTiquete);
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrdenTrabajoConfiguracion.IndiceTiquete,
        })
        {
            // Dos ingenieros asignaron el mismo tiquete a la vez: el índice único decide.
            throw new ErrorConflicto(GestionOrdenes.CodigoTiqueteAsignado, "Otro ingeniero acaba de asignar este tiquete; ya tiene una orden de trabajo.");
        }
    }

    private IQueryable<OrdenTrabajo> Filtrar(Guid? tecnicoId)
    {
        var ordenes = contexto.Ordenes.AsNoTracking();
        return tecnicoId is { } id ? ordenes.Where(o => o.TecnicoId == id) : ordenes;
    }

    private IQueryable<OrdenLeida> Leer(IQueryable<OrdenTrabajo> ordenes) =>
        from o in ordenes
        join e in contexto.Equipos on o.EquipoId equals e.Id
        join tipo in contexto.TiposEquipo on e.TipoEquipoId equals tipo.Id
        join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
        join tec in contexto.Usuarios on o.TecnicoId equals tec.Id
        join t in contexto.Tiquetes on o.TiqueteId equals t.Id into tiquetes
        from t in tiquetes.DefaultIfEmpty()
        select new OrdenLeida(
            o,
            e.PlacaInventario,
            e.NumeroSerie,
            tipo.Nombre,
            u.Nombre,
            tec.Nombres + " " + tec.Apellidos,
            t == null ? null : t.Codigo,
            t == null ? null : t.TipoFalla);
}
