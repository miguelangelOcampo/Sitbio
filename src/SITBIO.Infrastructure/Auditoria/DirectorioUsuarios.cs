using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Auditoria;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Auditoria;

/// <summary>Nombres de usuarios del tenant del token; el filtro global de tenant aplica (RN-008).</summary>
public sealed class DirectorioUsuarios(SitbioDbContext contexto) : IDirectorioUsuarios
{
    public async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await contexto.Usuarios
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombres + " " + u.Apellidos, cancellationToken);
    }
}
