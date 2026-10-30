using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>rol</c> (script 01 y V004). Global: sin tenant_id ni filtro.</summary>
public sealed class RolConfiguracion : IEntityTypeConfiguration<Rol>
{
    public void Configure(EntityTypeBuilder<Rol> rol)
    {
        rol.ToTable("rol");
        rol.HasKey(r => r.Id);
        rol.Property(r => r.Id).HasColumnName("id_rol").ValueGeneratedOnAdd();
        rol.Property(r => r.Codigo).HasColumnName("codigo").HasMaxLength(5).IsRequired();
        rol.Property(r => r.Nombre).HasColumnName("nombre").HasMaxLength(50).IsRequired();
        rol.Property(r => r.Descripcion).HasColumnName("descripcion").HasMaxLength(200).IsRequired();
        rol.HasIndex(r => r.Codigo).IsUnique().HasDatabaseName("uq_rol_codigo");
    }
}
