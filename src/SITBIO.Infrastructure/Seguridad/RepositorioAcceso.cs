using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Seguridad;

public sealed class RepositorioAcceso(SitbioDbContext contexto) : IRepositorioAcceso
{
    public async Task<UsuarioParaAcceso?> BuscarPorCorreoAsync(string correo, CancellationToken cancellationToken)
    {
        // Única consulta sin filtro de tenant: el login aún no tiene token. El correo es único (V005).
        var usuario = await contexto.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .SingleOrDefaultAsync(u => u.Correo == correo, cancellationToken);
        if (usuario is null)
        {
            return null;
        }

        var institucion = await contexto.Tenants
            .Where(t => t.Id == usuario.TenantId)
            .Select(t => new { t.EstadoActivo, t.Nombre })
            .SingleOrDefaultAsync(cancellationToken);

        return new UsuarioParaAcceso(usuario, usuario.Rol!.Codigo, institucion?.EstadoActivo ?? false, institucion?.Nombre ?? string.Empty);
    }

    public async Task GuardarAsync(Usuario usuario, SesionToken? sesion, CancellationToken cancellationToken)
    {
        if (sesion is not null)
        {
            contexto.Sesiones.Add(sesion);
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }
}
