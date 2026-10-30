using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>tenant</c> (script 01). Es la raíz del aislamiento: no lleva filtro.</summary>
public sealed class TenantConfiguracion : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> tenant)
    {
        tenant.ToTable("tenant");
        tenant.HasKey(t => t.Id);
        tenant.Property(t => t.Id).HasColumnName("id_tenant");
        tenant.Property(t => t.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
        tenant.Property(t => t.EstadoActivo).HasColumnName("estado_activo");
    }
}
