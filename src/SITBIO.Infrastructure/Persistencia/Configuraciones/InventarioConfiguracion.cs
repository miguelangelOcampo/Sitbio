using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Persistencia.Configuraciones;

/// <summary>Tabla <c>estado_equipo</c> (script 01). Global: sin tenant_id ni filtro.</summary>
public sealed class EstadoEquipoConfiguracion : IEntityTypeConfiguration<EstadoEquipo>
{
    public void Configure(EntityTypeBuilder<EstadoEquipo> estado)
    {
        estado.ToTable("estado_equipo");
        estado.HasKey(e => e.Id);
        estado.Property(e => e.Id).HasColumnName("id_estado").ValueGeneratedNever();
        estado.Property(e => e.Codigo).HasColumnName("codigo").HasMaxLength(20).IsRequired();
        estado.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50).IsRequired();
        estado.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(200);
    }
}

/// <summary>Tabla <c>tipo_equipo</c> (script 01 + V008). El filtro por tenant lo agrega <see cref="SitbioDbContext"/>.</summary>
public sealed class TipoEquipoConfiguracion : IEntityTypeConfiguration<TipoEquipo>
{
    public const string IndiceNombre = "uq_tipo_equipo_tenant_nombre";

    public void Configure(EntityTypeBuilder<TipoEquipo> tipo)
    {
        tipo.ToTable("tipo_equipo");
        tipo.HasKey(t => t.Id);
        tipo.Property(t => t.Id).HasColumnName("id_tipo_equipo");
        tipo.Property(t => t.Nombre).HasColumnName("nombre").HasMaxLength(TipoEquipo.LongitudMaximaNombre).IsRequired();
        tipo.Property(t => t.Descripcion).HasColumnName("descripcion").HasMaxLength(200);
        tipo.Property(t => t.EstadoActivo).HasColumnName("estado_activo");
        tipo.Property(t => t.ClaseRiesgoDefecto).HasColumnName("clase_riesgo_defecto").HasMaxLength(3);
        tipo.Property(t => t.RequiereCalibracion).HasColumnName("requiere_calibracion");
        tipo.Property(t => t.PeriodicidadCalibracionMeses).HasColumnName("periodicidad_calibracion_meses");

        tipo.HasIndex(t => new { t.TenantId, t.Nombre }).IsUnique().HasDatabaseName(IndiceNombre);
    }
}

/// <summary>Tabla <c>ubicacion</c> (script 01).</summary>
public sealed class UbicacionConfiguracion : IEntityTypeConfiguration<Ubicacion>
{
    public const string IndiceNombre = "uq_ubicacion_tenant_nombre";

    public void Configure(EntityTypeBuilder<Ubicacion> ubicacion)
    {
        ubicacion.ToTable("ubicacion");
        ubicacion.HasKey(u => u.Id);
        ubicacion.Property(u => u.Id).HasColumnName("id_ubicacion");
        ubicacion.Property(u => u.Nombre).HasColumnName("nombre").HasMaxLength(Ubicacion.LongitudMaximaNombre).IsRequired();
        ubicacion.Property(u => u.Descripcion).HasColumnName("descripcion").HasMaxLength(Ubicacion.LongitudMaximaDescripcion);
        ubicacion.Property(u => u.EstadoActivo).HasColumnName("estado_activo");

        ubicacion.HasIndex(u => new { u.TenantId, u.Nombre }).IsUnique().HasDatabaseName(IndiceNombre);
    }
}

/// <summary>Tabla <c>proveedor</c> (script 01), con sus datos de contacto (HU-025). NIT único por tenant.</summary>
public sealed class ProveedorConfiguracion : IEntityTypeConfiguration<Proveedor>
{
    public const string IndiceNit = "uq_proveedor_tenant_nit";

    public void Configure(EntityTypeBuilder<Proveedor> proveedor)
    {
        proveedor.ToTable("proveedor");
        proveedor.HasKey(p => p.Id);
        proveedor.Property(p => p.Id).HasColumnName("id_proveedor");
        proveedor.Property(p => p.Nombre).HasColumnName("nombre").HasMaxLength(Proveedor.LongitudMaximaNombre).IsRequired();
        proveedor.Property(p => p.Nit).HasColumnName("nit").HasMaxLength(Proveedor.LongitudMaximaNit).IsRequired();
        proveedor.Property(p => p.TipoProveedor).HasColumnName("tipo_proveedor").HasMaxLength(Proveedor.LongitudMaximaTipo).IsRequired();
        proveedor.Property(p => p.NombreContacto).HasColumnName("nombre_contacto").HasMaxLength(Proveedor.LongitudMaximaContacto);
        proveedor.Property(p => p.Correo).HasColumnName("correo").HasMaxLength(Proveedor.LongitudMaximaCorreo);
        proveedor.Property(p => p.Telefono).HasColumnName("telefono").HasMaxLength(Proveedor.LongitudMaximaTelefono);
        proveedor.Property(p => p.EstadoActivo).HasColumnName("estado_activo");

        proveedor.HasIndex(p => new { p.TenantId, p.Nit }).IsUnique().HasDatabaseName(IndiceNit);
    }
}

/// <summary>
/// Tabla <c>tipo_falla</c> (V009). El índice único de la base usa lower(nombre); aquí se declara
/// solo con su nombre para reconocer la violación al guardar.
/// </summary>
public sealed class TipoFallaConfiguracion : IEntityTypeConfiguration<TipoFalla>
{
    public const string IndiceNombre = "uq_tipo_falla_tenant_nombre";

    public void Configure(EntityTypeBuilder<TipoFalla> tipo)
    {
        tipo.ToTable("tipo_falla");
        tipo.HasKey(t => t.Id);
        tipo.Property(t => t.Id).HasColumnName("id_tipo_falla");
        tipo.Property(t => t.Nombre).HasColumnName("nombre").HasMaxLength(TipoFalla.LongitudMaximaNombre).IsRequired();
        tipo.Property(t => t.EstadoActivo).HasColumnName("estado_activo");
    }
}

/// <summary>
/// Tabla <c>equipo_biomedico</c> (script 01 + V008). Serie y placa únicas por tenant (RN-001).
/// La llave foránea de la ubicación o del tipo no comprueba el tenant: eso lo valida la aplicación.
/// </summary>
public sealed class EquipoBiomedicoConfiguracion : IEntityTypeConfiguration<EquipoBiomedico>
{
    public const string IndicePlaca = "uq_equipo_tenant_placa";
    public const string IndiceSerie = "uq_equipo_tenant_serie";

    public void Configure(EntityTypeBuilder<EquipoBiomedico> equipo)
    {
        equipo.ToTable("equipo_biomedico");
        equipo.HasKey(e => e.Id);
        equipo.Property(e => e.Id).HasColumnName("id_equipo");
        equipo.Property(e => e.TipoEquipoId).HasColumnName("tipo_equipo_id");
        equipo.Property(e => e.UbicacionId).HasColumnName("ubicacion_id");
        equipo.Property(e => e.EstadoEquipoId).HasColumnName("estado_equipo_id");
        equipo.Property(e => e.ProveedorId).HasColumnName("proveedor_id");
        equipo.Property(e => e.NumeroSerie).HasColumnName("numero_serie").HasMaxLength(EquipoBiomedico.LongitudMaximaSerie).IsRequired();
        equipo.Property(e => e.PlacaInventario).HasColumnName("placa_inventario").HasMaxLength(EquipoBiomedico.LongitudMaximaPlaca).IsRequired();
        equipo.Property(e => e.Marca).HasColumnName("marca").HasMaxLength(EquipoBiomedico.LongitudMaximaMarca).IsRequired();
        equipo.Property(e => e.Modelo).HasColumnName("modelo").HasMaxLength(EquipoBiomedico.LongitudMaximaModelo).IsRequired();
        equipo.Property(e => e.FechaAdquisicion).HasColumnName("fecha_adquisicion");
        equipo.Property(e => e.FechaBaja).HasColumnName("fecha_baja");
        equipo.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");

        equipo.Property(e => e.TipoAutorizacion).HasColumnName("tipo_autorizacion").HasMaxLength(30);
        equipo.Property(e => e.RegistroInvima).HasColumnName("registro_invima").HasMaxLength(EquipoBiomedico.LongitudMaximaRegistro);
        equipo.Property(e => e.VenceRegistro).HasColumnName("vence_registro");
        equipo.Property(e => e.ClaseRiesgo).HasColumnName("clase_riesgo").HasMaxLength(3);
        equipo.Property(e => e.RequiereCalibracion).HasColumnName("requiere_calibracion");
        equipo.Property(e => e.PeriodicidadCalibracionMeses).HasColumnName("periodicidad_calibracion_meses");
        equipo.Property(e => e.FotoDocumentoId).HasColumnName("foto_documento_id");
        equipo.Ignore(e => e.DadoDeBaja);

        equipo.HasOne<TipoEquipo>().WithMany().HasForeignKey(e => e.TipoEquipoId).IsRequired();
        equipo.HasOne<Ubicacion>().WithMany().HasForeignKey(e => e.UbicacionId).IsRequired();
        equipo.HasOne<EstadoEquipo>().WithMany().HasForeignKey(e => e.EstadoEquipoId).IsRequired();
        equipo.HasOne<Proveedor>().WithMany().HasForeignKey(e => e.ProveedorId);

        equipo.HasIndex(e => new { e.TenantId, e.PlacaInventario }).IsUnique().HasDatabaseName(IndicePlaca);
        equipo.HasIndex(e => new { e.TenantId, e.NumeroSerie }).IsUnique().HasDatabaseName(IndiceSerie);
    }
}

/// <summary>Tabla <c>plan_preventivo</c> (script 01 + V008).</summary>
public sealed class PlanPreventivoConfiguracion : IEntityTypeConfiguration<PlanPreventivo>
{
    public void Configure(EntityTypeBuilder<PlanPreventivo> plan)
    {
        plan.ToTable("plan_preventivo");
        plan.HasKey(p => p.Id);
        plan.Property(p => p.Id).HasColumnName("id_plan");
        plan.Property(p => p.EquipoId).HasColumnName("equipo_id");
        plan.Property(p => p.Descripcion).HasColumnName("descripcion").HasMaxLength(300).IsRequired();
        plan.Property(p => p.PeriodicidadDias).HasColumnName("periodicidad_dias");
        plan.Property(p => p.ProximaFecha).HasColumnName("proxima_fecha");
        plan.Property(p => p.UltimaEjecucion).HasColumnName("ultima_ejecucion");
        plan.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(15).IsRequired();
        plan.Property(p => p.FechaCreacion).HasColumnName("fecha_creacion");

        plan.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(p => p.EquipoId).IsRequired();
    }
}

/// <summary>Tabla <c>calibracion</c> (script 01; HU-022). El certificado en documento_equipo llega con los documentos.</summary>
public sealed class RegistroCalibracionConfiguracion : IEntityTypeConfiguration<RegistroCalibracion>
{
    public void Configure(EntityTypeBuilder<RegistroCalibracion> calibracion)
    {
        calibracion.ToTable("calibracion");
        calibracion.HasKey(c => c.Id);
        calibracion.Property(c => c.Id).HasColumnName("id_calibracion");
        calibracion.Property(c => c.EquipoId).HasColumnName("equipo_id");
        calibracion.Property(c => c.ProveedorId).HasColumnName("proveedor_id");
        calibracion.Property(c => c.OrdenId).HasColumnName("orden_id");
        calibracion.Property(c => c.RegistradoPorId).HasColumnName("registrado_por_id");
        calibracion.Property(c => c.AprobadoPorId).HasColumnName("aprobado_por_id");
        calibracion.Property(c => c.FechaCalibracion).HasColumnName("fecha_calibracion");
        calibracion.Property(c => c.PatronReferencia).HasColumnName("patron_referencia").HasMaxLength(RegistroCalibracion.LongitudMaximaPatron).IsRequired();
        calibracion.Property(c => c.Resultado).HasColumnName("resultado").HasMaxLength(15).IsRequired();
        calibracion.Property(c => c.NumeroCertificado).HasColumnName("numero_certificado").HasMaxLength(RegistroCalibracion.LongitudMaximaCertificado);
        calibracion.Property(c => c.FechaVencimiento).HasColumnName("fecha_vencimiento");
        calibracion.Ignore(c => c.Aprobada);

        calibracion.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(c => c.EquipoId).IsRequired();
        calibracion.HasOne<Proveedor>().WithMany().HasForeignKey(c => c.ProveedorId);
        calibracion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.RegistradoPorId).IsRequired();
        calibracion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.AprobadoPorId);
    }
}

/// <summary>Historial de estados (script 01; RF-014). Solo inserciones.</summary>
public sealed class HistorialEstadoEquipoConfiguracion : IEntityTypeConfiguration<HistorialEstadoEquipo>
{
    public void Configure(EntityTypeBuilder<HistorialEstadoEquipo> historial)
    {
        historial.ToTable("historial_estado_equipo");
        historial.HasKey(h => h.Id);
        historial.Property(h => h.Id).HasColumnName("id_historial");
        historial.Property(h => h.EquipoId).HasColumnName("equipo_id");
        historial.Property(h => h.EstadoAnteriorId).HasColumnName("estado_anterior_id");
        historial.Property(h => h.NuevoEstadoId).HasColumnName("nuevo_estado_id");
        historial.Property(h => h.UsuarioId).HasColumnName("usuario_id");
        historial.Property(h => h.Motivo).HasColumnName("motivo").HasMaxLength(HistorialEstadoEquipo.LongitudMaximaMotivo);
        historial.Property(h => h.FechaCambio).HasColumnName("fecha_cambio");

        historial.HasOne<EquipoBiomedico>().WithMany().HasForeignKey(h => h.EquipoId).IsRequired();
        historial.HasOne<EstadoEquipo>().WithMany().HasForeignKey(h => h.EstadoAnteriorId);
        historial.HasOne<EstadoEquipo>().WithMany().HasForeignKey(h => h.NuevoEstadoId).IsRequired();
        historial.HasOne<Usuario>().WithMany().HasForeignKey(h => h.UsuarioId).IsRequired();
    }
}
