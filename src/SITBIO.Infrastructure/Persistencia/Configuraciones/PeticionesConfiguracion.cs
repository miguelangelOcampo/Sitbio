using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>tiquete</c> (script 01; HU-003). El filtro por tenant lo agrega <see cref="SitbioDbContext"/>.</summary>
public sealed class TiqueteConfiguracion : IEntityTypeConfiguration<Tiquete>
{
    public const string IndiceCodigo = "uq_tiquete_tenant_codigo";

    public void Configure(EntityTypeBuilder<Tiquete> tiquete)
    {
        tiquete.ToTable("tiquete");
        tiquete.HasKey(t => t.Id);
        tiquete.Property(t => t.Id).HasColumnName("id_tiquete");
        tiquete.Property(t => t.EquipoId).HasColumnName("equipo_id");
        tiquete.Property(t => t.ReportanteId).HasColumnName("reportante_id");
        tiquete.Property(t => t.Codigo).HasColumnName("codigo").HasMaxLength(20).IsRequired();
        tiquete.Property(t => t.TipoFalla).HasColumnName("tipo_falla").HasMaxLength(TipoFalla.LongitudMaximaNombre).IsRequired();
        tiquete.Property(t => t.Descripcion).HasColumnName("descripcion").IsRequired();
        tiquete.Property(t => t.Criticidad).HasColumnName("criticidad").HasMaxLength(10).IsRequired();
        tiquete.Property(t => t.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
        tiquete.Property(t => t.FechaReporte).HasColumnName("fecha_reporte");
        tiquete.Property(t => t.FechaCierre).HasColumnName("fecha_cierre");
        tiquete.Ignore(t => t.EsCritica);
        tiquete.Ignore(t => t.PrioridadSugerida);

        tiquete.HasIndex(t => new { t.TenantId, t.Codigo }).IsUnique().HasDatabaseName(IndiceCodigo);
        tiquete.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(t => t.EquipoId).IsRequired();
        tiquete.HasOne<Usuario>().WithMany().HasForeignKey(t => t.ReportanteId).IsRequired();
    }
}

/// <summary>Tabla <c>historial_tiquete</c> (script 01; RF-017). Solo inserciones.</summary>
public sealed class HistorialTiqueteConfiguracion : IEntityTypeConfiguration<HistorialTiquete>
{
    public void Configure(EntityTypeBuilder<HistorialTiquete> historial)
    {
        historial.ToTable("historial_tiquete");
        historial.HasKey(h => h.Id);
        historial.Property(h => h.Id).HasColumnName("id_historial");
        historial.Property(h => h.TiqueteId).HasColumnName("tiquete_id");
        historial.Property(h => h.UsuarioId).HasColumnName("usuario_id");
        historial.Property(h => h.EstadoAnterior).HasColumnName("estado_anterior").HasMaxLength(20);
        historial.Property(h => h.NuevoEstado).HasColumnName("nuevo_estado").HasMaxLength(20).IsRequired();
        historial.Property(h => h.Comentario).HasColumnName("comentario").HasMaxLength(HistorialTiquete.LongitudMaximaComentario);
        historial.Property(h => h.FechaCambio).HasColumnName("fecha_cambio");

        historial.HasOne<Tiquete>().WithMany().HasForeignKey(h => h.TiqueteId).IsRequired();
        historial.HasOne<Usuario>().WithMany().HasForeignKey(h => h.UsuarioId).IsRequired();
    }
}
