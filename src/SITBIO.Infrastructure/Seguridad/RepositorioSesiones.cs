using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>Sesiones sobre PostgreSQL (<c>sesion_token</c>); salvo la validación, todo pasa por el filtro de tenant.</summary>
public sealed class RepositorioSesiones(SitbioDbContext contexto) : IRepositorioSesiones
{
    public Task<bool> EstaActivaAsync(Guid tenantId, string jti, CancellationToken cancellationToken)
    {
        // Sin filtro global: se valida antes de fijar el usuario de la solicitud. El tenant
        // viene del token ya firmado y verificado, y se exige explícitamente.
        return contexto.Sesiones.IgnoreQueryFilters()
            .AnyAsync(s => s.Jti == jti && s.TenantId == tenantId && s.FechaRevocacion == null, cancellationToken);
    }

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
