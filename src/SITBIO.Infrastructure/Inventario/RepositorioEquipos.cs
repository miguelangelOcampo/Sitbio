using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Persistencia.Configuraciones;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>Equipos sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioEquipos(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioEquipos
{
    private const string Escape = "\\";

    public async Task<(IReadOnlyList<EquipoLeido> Items, int Total)> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken)
    {
        var equipos = contexto.Equipos.AsNoTracking();
        if (filtro.Placa is { } placa)
        {
            var patron = Contiene(placa);
            equipos = equipos.Where(e => EF.Functions.ILike(e.PlacaInventario, patron, Escape));
        }

        if (filtro.Serie is { } serie)
        {
            var patron = Contiene(serie);
            equipos = equipos.Where(e => EF.Functions.ILike(e.NumeroSerie, patron, Escape));
        }

        if (filtro.Estado is { } estado)
        {
            equipos = equipos.Where(e => contexto.EstadosEquipo.Any(s => s.Id == e.EstadoEquipoId && s.Codigo == estado));
        }

        if (filtro.Clase is { } clase)
        {
            equipos = equipos.Where(e => e.ClaseRiesgo == clase);
        }

        if (filtro.Calibracion is { } calibracion)
        {
            equipos = equipos.Where(e => e.RequiereCalibracion == calibracion);
        }

        var total = await equipos.CountAsync(cancellationToken);
        var pagina = equipos
            .OrderBy(e => e.PlacaInventario).ThenBy(e => e.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano);
        var items = await Leer(pagina).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<EquipoLeido?> ObtenerAsync(Guid id, bool paraEditar, CancellationToken cancellationToken)
    {
        var consulta = Leer(contexto.Equipos.Where(e => e.Id == id));
        return (paraEditar ? consulta : consulta.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<TipoEquipo?> TipoEquipoAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.TiposEquipo.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ElementoCatalogo?> UbicacionAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Ubicaciones.Where(u => u.Id == id)
            .Select(u => new ElementoCatalogo(u.Id, u.Nombre, u.EstadoActivo))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ElementoCatalogo?> ProveedorAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Proveedores.Where(p => p.Id == id)
            .Select(p => new ElementoCatalogo(p.Id, p.Nombre, p.EstadoActivo))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Duplicados> DuplicadosAsync(string numeroSerie, string placaInventario, Guid? exceptoEquipoId, CancellationToken cancellationToken)
    {
        // Mismo criterio que los índices únicos: comparación exacta dentro del tenant.
        var otros = contexto.Equipos.Where(e => exceptoEquipoId == null || e.Id != exceptoEquipoId);
        var serie = await otros.AnyAsync(e => e.NumeroSerie == numeroSerie, cancellationToken);
        var placa = await otros.AnyAsync(e => e.PlacaInventario == placaInventario, cancellationToken);
        return new Duplicados(serie, placa);
    }

    public async Task<CapacidadEquipos> CapacidadEquiposAsync(CancellationToken cancellationToken)
    {
        // El tenant sale del token; tenant no lleva filtro global porque es la raíz del aislamiento.
        var tenantId = contextoUsuario.TenantId;
        var plan = await contexto.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Plan, t.MaxEquipos })
            .SingleAsync(cancellationToken);

        var activos = await contexto.Equipos.CountAsync(e => e.EstadoEquipoId != EstadosEquipo.Baja, cancellationToken);
        return new CapacidadEquipos(plan.Plan, plan.MaxEquipos, activos);
    }

    public void Agregar(EquipoBiomedico equipo) => contexto.Equipos.Add(equipo);

    public void Agregar(PlanPreventivo plan) => contexto.PlanesPreventivos.Add(plan);

    public void Agregar(HistorialEstadoEquipo registro) => contexto.HistorialEstados.Add(registro);

    public async Task<IReadOnlyList<HistorialEstadoEquipo>> HistorialAsync(Guid equipoId, CancellationToken cancellationToken) =>
        await contexto.HistorialEstados.AsNoTracking()
            .Where(h => h.EquipoId == equipoId)
            .OrderByDescending(h => h.FechaCambio).ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);

    public async Task GuardarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EquipoBiomedicoConfiguracion.IndicePlaca or EquipoBiomedicoConfiguracion.IndiceSerie,
        } postgres)
        {
            // Dos registros simultáneos pasaron la consulta previa: el índice único decide (RN-001).
            var dato = postgres.ConstraintName == EquipoBiomedicoConfiguracion.IndicePlaca ? "la placa de inventario" : "el número de serie";
            throw new ErrorConflicto(GestionEquipos.CodigoDuplicado, $"Ya existe un equipo con {dato} indicada en tu institución.");
        }
    }

    private IQueryable<EquipoLeido> Leer(IQueryable<EquipoBiomedico> equipos) =>
        from e in equipos
        join t in contexto.TiposEquipo on e.TipoEquipoId equals t.Id
        join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
        join s in contexto.EstadosEquipo on e.EstadoEquipoId equals s.Id
        select new EquipoLeido(
            e,
            t.Nombre,
            u.Nombre,
            contexto.Proveedores.Where(p => p.Id == e.ProveedorId).Select(p => p.Nombre).FirstOrDefault(),
            s.Codigo,
            s.Nombre,
            contexto.PlanesPreventivos
                .Where(p => p.EquipoId == e.Id && p.Estado == PlanPreventivo.Activo)
                .OrderBy(p => p.ProximaFecha)
                .FirstOrDefault());

    private static string Contiene(string texto) =>
        "%" + texto.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_") + "%";
}
