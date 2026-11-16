using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>tenant</c> (script 01 y V002). Es la raíz del aislamiento: no lleva filtro.</summary>
public sealed class TenantConfiguracion : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> tenant)
    {
        tenant.ToTable("tenant");
        tenant.HasKey(t => t.Id);
        tenant.Property(t => t.Id).HasColumnName("id_tenant");
        tenant.Property(t => t.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
        tenant.Property(t => t.Nit).HasColumnName("nit").HasMaxLength(20).IsRequired();
        tenant.Property(t => t.DiasAnticipacionAlerta).HasColumnName("dias_anticipacion_alerta");
        tenant.Property(t => t.EstadoActivo).HasColumnName("estado_activo");
        tenant.Property(t => t.Plan).HasColumnName("plan").HasMaxLength(20).IsRequired();
        tenant.Property(t => t.MaxUsuariosGestion).HasColumnName("max_usuarios_gestion");
        tenant.Property(t => t.MaxEquipos).HasColumnName("max_equipos");
        tenant.Property(t => t.MaxAlmacenamientoGb).HasColumnName("max_almacenamiento_gb");
        tenant.Property(t => t.VigenteHasta).HasColumnName("vigente_hasta");
    }
}
