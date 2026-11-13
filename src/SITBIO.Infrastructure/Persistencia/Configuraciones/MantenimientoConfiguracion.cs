using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Tabla <c>orden_trabajo</c> (script 01; HU-004, HU-005, HU-019, HU-021): asignación (o generación desde un
/// plan preventivo), ejecución (inicio y enlace con la intervención), cierre y aprobación con firma. El filtro por tenant lo agrega <see cref="SitbioDbContext"/>.
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
        orden.Property(o => o.PlanId).HasColumnName("plan_id");
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
        orden.Property(o => o.FechaCierre).HasColumnName("fecha_cierre");
        orden.Property(o => o.AprobadoPorId).HasColumnName("aprobado_por_id");
        orden.Property(o => o.FechaAprobacion).HasColumnName("fecha_aprobacion");
        orden.Property(o => o.FirmaAprobacion).HasColumnName("firma_aprobacion").HasMaxLength(OrdenTrabajo.LongitudMaximaFirma);

        orden.HasIndex(o => o.TiqueteId).IsUnique().HasFilter("tiquete_id IS NOT NULL").HasDatabaseName(IndiceTiquete);
        orden.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(o => o.EquipoId).IsRequired();
        orden.HasOne<Tiquete>().WithMany().HasForeignKey(o => o.TiqueteId);
        orden.HasOne<PlanPreventivo>().WithMany().HasForeignKey(o => o.PlanId);
        orden.HasOne<Usuario>().WithMany().HasForeignKey(o => o.TecnicoId).IsRequired();
        orden.HasOne<Usuario>().WithMany().HasForeignKey(o => o.AsignadoPorId);
        orden.HasOne<Usuario>().WithMany().HasForeignKey(o => o.AprobadoPorId);
    }
}
