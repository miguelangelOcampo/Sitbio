using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>usuario</c> (script 01). El filtro por tenant lo agrega <see cref="SitbioDbContext"/>.</summary>
public sealed class UsuarioConfiguracion : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> usuario)
    {
        usuario.ToTable("usuario");
        usuario.HasKey(u => u.Id);
        usuario.Property(u => u.Id).HasColumnName("id_usuario");
        usuario.Property(u => u.RolId).HasColumnName("rol_id");
        usuario.Property(u => u.Nombres).HasColumnName("nombres").HasMaxLength(100).IsRequired();
        usuario.Property(u => u.Apellidos).HasColumnName("apellidos").HasMaxLength(100).IsRequired();
        usuario.Property(u => u.Correo).HasColumnName("correo").HasMaxLength(100).IsRequired();
        usuario.Property(u => u.ContrasenaHash).HasColumnName("contrasena_hash").HasMaxLength(100).IsRequired();
        usuario.Property(u => u.Telefono).HasColumnName("telefono").HasMaxLength(30);
        usuario.Property(u => u.IntentosFallidos).HasColumnName("intentos_fallidos");
        usuario.Property(u => u.EstadoBloqueo).HasColumnName("estado_bloqueo");
        usuario.Property(u => u.CambioContrasenaObligatorio).HasColumnName("cambio_contrasena_obligatorio");
        usuario.Property(u => u.EstadoActivo).HasColumnName("estado_activo");
        usuario.Property(u => u.UltimoAcceso).HasColumnName("ultimo_acceso");
        usuario.Property(u => u.FechaCreacion).HasColumnName("fecha_creacion");

        usuario.HasOne(u => u.Rol).WithMany().HasForeignKey(u => u.RolId).IsRequired();
        usuario.HasIndex(u => new { u.TenantId, u.Correo }).IsUnique().HasDatabaseName("uq_usuario_tenant_correo");
    }
}
