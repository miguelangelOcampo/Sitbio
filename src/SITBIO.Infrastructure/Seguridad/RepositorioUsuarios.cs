using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>Usuarios sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioUsuarios(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioUsuarios
{
    private static readonly string[] RolesGestion = [.. CodigosRol.Gestion];

    public async Task<CapacidadGestion> CapacidadGestionAsync(CancellationToken cancellationToken)
    {
        // El tenant sale del token; tenant no lleva filtro global porque es la raíz del aislamiento.
        var tenantId = contextoUsuario.TenantId;
        var plan = await contexto.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Plan, t.MaxUsuariosGestion })
            .SingleAsync(cancellationToken);

        // Usuarios activos con rol de gestión; AC-04 y AC-07 no cuentan. Los usuarios ya llevan el filtro por tenant.
        var activos = await contexto.Usuarios
            .CountAsync(u => u.EstadoActivo && RolesGestion.Contains(u.Rol!.Codigo), cancellationToken);

        return new CapacidadGestion(plan.Plan, plan.MaxUsuariosGestion, activos);
    }

    public async Task<(IReadOnlyList<(Usuario Usuario, string CodigoRol)> Items, int Total)> ListarAsync(
        int pagina, int tamano, CancellationToken cancellationToken)
    {
        var consulta = contexto.Usuarios.Include(u => u.Rol);
        var total = await consulta.CountAsync(cancellationToken);
        var usuarios = await consulta
            .OrderBy(u => u.Apellidos).ThenBy(u => u.Nombres).ThenBy(u => u.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(cancellationToken);

        return (usuarios.Select(u => (u, u.Rol!.Codigo)).ToList(), total);
    }

    public async Task<(Usuario Usuario, string CodigoRol)?> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await contexto.Usuarios.Include(u => u.Rol).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        return usuario is null ? null : (usuario, usuario.Rol!.Codigo);
    }

    public async Task<RolAsignable?> RolPorCodigoAsync(string codigo, CancellationToken cancellationToken) =>
        await contexto.Roles
            .Where(r => r.Codigo == codigo)
            .Select(r => new RolAsignable(r.Id, r.Codigo))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Sin filtro de tenant: el correo es único en todo el sistema (V005, ADR-011). Solo devuelve sí o no.</summary>
    public Task<bool> CorreoEnUsoAsync(string correo, Guid? exceptoUsuarioId, CancellationToken cancellationToken)
    {
        return contexto.Usuarios.IgnoreQueryFilters()
            .AnyAsync(u => u.Correo == correo && u.Id != exceptoUsuarioId, cancellationToken);
    }

    public void Agregar(Usuario usuario) => contexto.Usuarios.Add(usuario);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
