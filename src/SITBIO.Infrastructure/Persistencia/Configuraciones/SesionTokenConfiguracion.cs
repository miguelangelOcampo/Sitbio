using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>sesion_token</c> (script 01). El filtro por tenant lo agrega <see cref="SitbioDbContext"/>.</summary>
public sealed class SesionTokenConfiguracion : IEntityTypeConfiguration<SesionToken>
{
    public void Configure(EntityTypeBuilder<SesionToken> sesion)
    {
        sesion.ToTable("sesion_token");
        sesion.HasKey(s => s.Id);
        sesion.Property(s => s.Id).HasColumnName("id_sesion");
        sesion.Property(s => s.UsuarioId).HasColumnName("usuario_id");
        sesion.Property(s => s.Jti).HasColumnName("jti").HasMaxLength(SesionToken.LongitudMaximaJti).IsRequired();
        sesion.Property(s => s.FechaEmision).HasColumnName("fecha_emision");
        sesion.Property(s => s.FechaExpiracion).HasColumnName("fecha_expiracion");
        sesion.Property(s => s.FechaRevocacion).HasColumnName("fecha_revocacion");
        sesion.Property(s => s.MotivoRevocacion).HasColumnName("motivo_revocacion").HasMaxLength(200);

        sesion.HasOne<Usuario>().WithMany().HasForeignKey(s => s.UsuarioId).IsRequired();
        sesion.HasIndex(s => s.Jti).IsUnique().HasDatabaseName("uq_sesion_jti");
    }
}
