using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Tabla <c>orden_trabajo</c> (script 01; HU-004, HU-005). Se mapea lo que usan la asignación y la
/// ejecución (inicio y enlace con la intervención); el cierre y la aprobación llegan con HU-021. El filtro por
/// tenant lo agrega <see cref="SitbioDbContext"/>.
/// </summary>
public sealed class OrdenTrabajoConfiguracion : IEntityTypeConfiguration<OrdenTrabajo>
{
    /// <summary>Índice único parcial: un tiquete tiene a lo sumo una orden.</summary>
    public const string IndiceTiquete = "uq_orden_tiquete";

    public void Configure(EntityTypeBuilder<OrdenTrabajo> orden)
    {
        orden.ToTable("orden_trabajo");
        orden.HasKey(o => o.Id);
        orden.Property(o => o.Id).HasColumnName("id_orden");
        orden.Property(o => o.EquipoId).HasColumnName("equipo_id");
        orden.Property(o => o.TiqueteId).HasColumnName("tiquete_id");
        orden.Property(o => o.TecnicoId).HasColumnName("tecnico_id");
        orden.Property(o => o.AsignadoPorId).HasColumnName("asignado_por_id");
        orden.Property(o => o.TipoMantenimiento).HasColumnName("tipo_mantenimiento").HasMaxLength(12).IsRequired();
        orden.Property(o => o.Prioridad).HasColumnName("prioridad").HasMaxLength(10).IsRequired();
        orden.Property(o => o.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
        orden.Property(o => o.FechaProgramada).HasColumnName("fecha_programada");
        orden.Property(o => o.FechaAsignacion).HasColumnName("fecha_asignacion");
        orden.Property(o => o.FechaCreacion).HasColumnName("fecha_creacion");
        orden.Property(o => o.FechaInicio).HasColumnName("fecha_inicio");
        orden.Property(o => o.IntervencionId).HasColumnName("intervencion_id").HasMaxLength(24);

        orden.HasIndex(o => o.TiqueteId).IsUnique().HasFilter("tiquete_id IS NOT NULL").HasDatabaseName(IndiceTiquete);
        orden.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(o => o.EquipoId).IsRequired();
        orden.HasOne<Tiquete>().WithMany().HasForeignKey(o => o.TiqueteId);
        orden.HasOne<Usuario>().WithMany().HasForeignKey(o => o.TecnicoId).IsRequired();
        orden.HasOne<Usuario>().WithMany().HasForeignKey(o => o.AsignadoPorId);
    }
}
