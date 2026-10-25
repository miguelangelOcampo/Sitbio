namespace Sitbio.Domain.Comun;

/// <summary>
/// Base de las entidades que pertenecen a una institución (tenant).
/// Toda consulta y escritura se filtra por <see cref="TenantId"/> (RN-008, ADR-004).
/// </summary>
public abstract class EntidadTenant
{
    protected EntidadTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("La entidad debe pertenecer a una institución.", nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public Guid Id { get; protected set; } = Guid.NewGuid();

    public Guid TenantId { get; }
}
