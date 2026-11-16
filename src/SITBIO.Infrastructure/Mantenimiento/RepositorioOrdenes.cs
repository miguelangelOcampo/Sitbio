using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
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

    public async Task<IReadOnlyList<TecnicoConCarga>> TecnicosAsync(CancellationToken cancellationToken)
    {
        var abiertos = EstadosOrden.Abiertos.ToList();
        return await (from u in contexto.Usuarios.AsNoTracking()
                      join r in contexto.Roles on u.RolId equals r.Id
                      where r.Codigo == CodigosRol.TecnicoMantenimiento && u.EstadoActivo
                      let carga = contexto.Ordenes.Count(o => o.TecnicoId == u.Id && abiertos.Contains(o.Estado))
                      orderby carga, u.Nombres, u.Apellidos, u.Id
                      select new TecnicoConCarga(u.Id, u.Nombres + " " + u.Apellidos, carga))
            .ToListAsync(cancellationToken);
    }

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

    public Task<OrdenTrabajo?> OrdenAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Ordenes.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Guid?> TipoEquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.AsNoTracking().Where(e => e.Id == equipoId).Select(e => (Guid?)e.TipoEquipoId).SingleOrDefaultAsync(cancellationToken);

    public async Task GuardarAsync(HistorialTiquete? cambioTiquete, CancellationToken cancellationToken)
    {
        if (cambioTiquete is not null)
        {
            contexto.HistorialTiquetes.Add(cambioTiquete);
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }

    public async Task<EquipoDeLaOrden?> EquipoAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        // El filtro global deja solo los equipos del tenant del token (RN-008).
        var leido = await (from e in contexto.Equipos
                           join tipo in contexto.TiposEquipo on e.TipoEquipoId equals tipo.Id
                           join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
                           where e.Id == equipoId
                           select new { Equipo = e, Tipo = tipo.Nombre, Ubicacion = u.Nombre })
            .SingleOrDefaultAsync(cancellationToken);
        if (leido is null)
        {
            return null;
        }

        // Vigencia del equipo: el vencimiento más lejano de sus calibraciones (HU-022, RN-007).
        var vence = await contexto.Calibraciones.AsNoTracking()
            .Where(c => c.EquipoId == equipoId)
            .MaxAsync(c => (DateOnly?)c.FechaVencimiento, cancellationToken);
        return new EquipoDeLaOrden(leido.Equipo, leido.Tipo, leido.Ubicacion, vence);
    }

    public Task<PlanPreventivo?> PlanAsync(Guid planId, CancellationToken cancellationToken) =>
        contexto.PlanesPreventivos.SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);

    public async Task GuardarAprobacionAsync(HistorialTiquete? cambioTiquete, HistorialEstadoEquipo? cambioEquipo, CancellationToken cancellationToken)
    {
        // La orden, el tiquete, el equipo y el plan vienen con seguimiento: un solo SaveChanges los guarda en una transacción.
        if (cambioTiquete is not null)
        {
            contexto.HistorialTiquetes.Add(cambioTiquete);
        }

        if (cambioEquipo is not null)
        {
            contexto.HistorialEstados.Add(cambioEquipo);
        }

        await contexto.SaveChangesAsync(cancellationToken);
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
