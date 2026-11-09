using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>Sesiones sobre PostgreSQL (<c>sesion_token</c>); salvo la validación, todo pasa por el filtro de tenant.</summary>
public sealed class RepositorioSesiones(SitbioDbContext contexto) : IRepositorioSesiones
{
    public async Task<EstadoSesion> ConsultarEstadoAsync(Guid tenantId, string jti, CancellationToken cancellationToken)
    {
        // Sin filtro global: se valida antes de fijar el usuario de la solicitud. El tenant
        // viene del token ya firmado y verificado, y se exige explícitamente en ambas tablas.
        var claveTemporal = await (
                from s in contexto.Sesiones.IgnoreQueryFilters()
                join u in contexto.Usuarios.IgnoreQueryFilters() on s.UsuarioId equals u.Id
                where s.Jti == jti && s.TenantId == tenantId && u.TenantId == tenantId && s.FechaRevocacion == null
                select (bool?)u.CambioContrasenaObligatorio)
            .SingleOrDefaultAsync(cancellationToken);

        return claveTemporal switch
        {
            null => EstadoSesion.Cerrada,
            true => EstadoSesion.ClaveTemporal,
            false => EstadoSesion.Activa,
        };
    }

    public async Task<IReadOnlyList<SesionToken>> VigentesDelUsuarioAsync(Guid usuarioId, DateTimeOffset momento, CancellationToken cancellationToken) =>
        await contexto.Sesiones
            .Where(s => s.UsuarioId == usuarioId && s.FechaRevocacion == null && s.FechaExpiracion > momento)
            .ToListAsync(cancellationToken);

    public Task<SesionToken?> ObtenerAsync(string jti, CancellationToken cancellationToken) =>
        contexto.Sesiones.SingleOrDefaultAsync(s => s.Jti == jti, cancellationToken);

    public async Task<UsuarioParaAcceso?> BuscarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await contexto.Usuarios.Include(u => u.Rol).SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
        if (usuario is null)
        {
            return null;
        }

        var institucionActiva = await contexto.Tenants
            .Where(t => t.Id == usuario.TenantId)
            .Select(t => t.EstadoActivo)
            .SingleOrDefaultAsync(cancellationToken);

        return new UsuarioParaAcceso(usuario, usuario.Rol!.Codigo, institucionActiva);
    }

    public void Agregar(SesionToken sesion) => contexto.Sesiones.Add(sesion);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
