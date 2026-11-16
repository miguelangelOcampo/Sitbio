using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Instituciones;

/// <summary>
/// La institución del token sobre PostgreSQL. La tabla tenant no lleva filtro global (es la raíz
/// del aislamiento), por eso cada consulta usa el tenant de <see cref="IContextoUsuario"/>.
/// </summary>
public sealed class RepositorioInstitucion(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioInstitucion
{
    private static readonly string[] RolesGestion = [.. CodigosRol.Gestion];

    public Task<Tenant?> ObtenerAsync(CancellationToken cancellationToken)
    {
        var tenantId = contextoUsuario.TenantId;
        return contexto.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    /// <summary>Las mismas reglas que bloquean crear (RN-009): AC-04 y AC-07 no cuentan; los equipos de baja tampoco.</summary>
    public async Task<UsoPlan> UsoAsync(CancellationToken cancellationToken)
    {
        // Usuarios y equipos ya llevan el filtro global por tenant.
        var usuarios = await contexto.Usuarios.CountAsync(u => u.EstadoActivo && RolesGestion.Contains(u.Rol!.Codigo), cancellationToken);
        var equipos = await contexto.Equipos.CountAsync(e => e.EstadoEquipoId != EstadosEquipo.Baja, cancellationToken);

        // documento_equipo aún no tiene entidad (llega con HU-015): consulta parametrizada por el tenant del token
        // (esquema SitbioDbContext.Esquema).
        var tenantId = contextoUsuario.TenantId;
        var bytes = await contexto.Database
            .SqlQuery<long>($"SELECT COALESCE(SUM(tamano_bytes), 0)::bigint AS \"Value\" FROM sitbio.documento_equipo WHERE tenant_id = {tenantId}")
            .SingleAsync(cancellationToken);

        return new UsoPlan(usuarios, equipos, bytes);
    }

    public Task<bool> NitEnUsoAsync(string nit, CancellationToken cancellationToken)
    {
        var tenantId = contextoUsuario.TenantId;
        return contexto.Tenants.AnyAsync(t => t.Nit == nit && t.Id != tenantId, cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
