using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>solicitud_servicio</c> (V002, V003). Sin filtro por tenant: el tenant es opcional.</summary>
public sealed class SolicitudServicioConfiguracion : IEntityTypeConfiguration<SolicitudServicio>
{
    public void Configure(EntityTypeBuilder<SolicitudServicio> solicitud)
    {
        solicitud.ToTable("solicitud_servicio");
        solicitud.HasKey(s => s.Id);
        solicitud.Property(s => s.Id).HasColumnName("id_solicitud").ValueGeneratedNever();
        solicitud.Property(s => s.Codigo).HasColumnName("codigo").HasMaxLength(20).IsRequired();
        solicitud.Property(s => s.Tipo).HasColumnName("tipo").HasMaxLength(20).IsRequired();
        solicitud.Property(s => s.TenantId).HasColumnName("tenant_id");
        solicitud.Property(s => s.Institucion).HasColumnName("institucion").HasMaxLength(150).IsRequired();
        solicitud.Property(s => s.Nit).HasColumnName("nit").HasMaxLength(20).IsRequired();
        solicitud.Property(s => s.Contacto).HasColumnName("contacto").HasMaxLength(120).IsRequired();
        solicitud.Property(s => s.Correo).HasColumnName("correo").HasMaxLength(150).IsRequired();
        solicitud.Property(s => s.Telefono).HasColumnName("telefono").HasMaxLength(30);
        solicitud.Property(s => s.PlanSolicitado).HasColumnName("plan_solicitado").HasMaxLength(20);
        solicitud.Property(s => s.Mensaje).HasColumnName("mensaje").HasMaxLength(1000);
        solicitud.Property(s => s.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
        solicitud.Property(s => s.FechaCreacion).HasColumnName("fecha_creacion");
    }
}
