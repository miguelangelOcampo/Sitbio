using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Mantenimiento;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Mantenimiento;

/// <summary>Calibraciones sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioCalibraciones(SitbioDbContext contexto) : IRepositorioCalibraciones
{
    public async Task<(IReadOnlyList<CalibracionLeida> Items, int Total)> ListarAsync(FiltroCalibraciones filtro, CancellationToken cancellationToken)
    {
        var calibraciones = contexto.Calibraciones.AsNoTracking();
        if (filtro.EquipoId is { } equipoId)
        {
            calibraciones = calibraciones.Where(c => c.EquipoId == equipoId);
        }

        var total = await calibraciones.CountAsync(cancellationToken);
        var pagina = calibraciones
            .OrderByDescending(c => c.FechaCalibracion)
            .ThenByDescending(c => c.FechaVencimiento)
            .ThenBy(c => c.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano);
        return (await Leer(pagina).ToListAsync(cancellationToken), total);
    }

    public Task<CalibracionLeida?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        Leer(contexto.Calibraciones.AsNoTracking().Where(c => c.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<RegistroCalibracion?> CalibracionAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Calibraciones.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<EquipoDeCalibracion?> EquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.AsNoTracking()
            .Where(e => e.Id == equipoId)
            .Select(e => new EquipoDeCalibracion(e.Id, e.PlacaInventario, e.FechaBaja != null, e.RequiereCalibracion, e.PeriodicidadCalibracionMeses))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExisteProveedorAsync(Guid proveedorId, CancellationToken cancellationToken) =>
        contexto.Proveedores.AnyAsync(p => p.Id == proveedorId, cancellationToken);

    public void Agregar(RegistroCalibracion calibracion) => contexto.Calibraciones.Add(calibracion);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);

    private IQueryable<CalibracionLeida> Leer(IQueryable<RegistroCalibracion> calibraciones) =>
        from c in calibraciones
        join e in contexto.Equipos on c.EquipoId equals e.Id
        join t in contexto.TiposEquipo on e.TipoEquipoId equals t.Id
        join u in contexto.Ubicaciones on e.UbicacionId equals u.Id
        join r in contexto.Usuarios on c.RegistradoPorId equals r.Id
        join p in contexto.Proveedores on c.ProveedorId equals (Guid?)p.Id into proveedores
        from p in proveedores.DefaultIfEmpty()
        join a in contexto.Usuarios on c.AprobadoPorId equals (Guid?)a.Id into aprobadores
        from a in aprobadores.DefaultIfEmpty()
        select new CalibracionLeida(
            c,
            e.PlacaInventario,
            t.Nombre,
            u.Nombre,
            p == null ? null : p.Nombre,
            r.Nombres + " " + r.Apellidos,
            a == null ? null : a.Nombres + " " + a.Apellidos);
}
