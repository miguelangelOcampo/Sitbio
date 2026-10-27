using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Domain.Comun;

namespace Sitbio.Infrastructure.Persistencia;

/// <summary>
/// Contexto de EF Core sobre el esquema <c>sitbio</c> de PostgreSQL (CMP-PSQL).
/// Toda entidad que hereda de <see cref="EntidadTenant"/> recibe un filtro global
/// con el tenant del usuario autenticado (RN-008, SEC-003). El esquema lo crean los
/// scripts de database/ (ADR-005); aquí no hay migraciones automáticas.
/// </summary>
public class SitbioDbContext : DbContext
{
    public const string Esquema = "sitbio";

    private static readonly MethodInfo MetodoFiltroTenant = typeof(SitbioDbContext)
        .GetMethod(nameof(AplicarFiltroTenant), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly IContextoUsuario _contextoUsuario;

    public SitbioDbContext(DbContextOptions<SitbioDbContext> options, IContextoUsuario contextoUsuario)
        : this((DbContextOptions)options, contextoUsuario)
    {
    }

    protected SitbioDbContext(DbContextOptions options, IContextoUsuario contextoUsuario)
        : base(options)
    {
        _contextoUsuario = contextoUsuario;
    }

    /// <summary>Tenant del token; EF Core lo evalúa en cada consulta.</summary>
    protected Guid TenantActual => _contextoUsuario.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        VerificarTenantDeLosCambios();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        VerificarTenantDeLosCambios();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SitbioDbContext).Assembly);

        var entidadesConTenant = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(EntidadTenant).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Select(t => t.ClrType)
            .ToList();

        foreach (var tipo in entidadesConTenant)
        {
            MetodoFiltroTenant.MakeGenericMethod(tipo).Invoke(this, [modelBuilder]);
        }
    }

    private void AplicarFiltroTenant<TEntidad>(ModelBuilder modelBuilder)
        where TEntidad : EntidadTenant
    {
        var entidad = modelBuilder.Entity<TEntidad>();
        entidad.Property(e => e.TenantId).HasColumnName("tenant_id");
        entidad.HasQueryFilter(e => e.TenantId == TenantActual);
    }

    /// <summary>Impide insertar o modificar registros de otro tenant (defensa adicional al filtro).</summary>
    private void VerificarTenantDeLosCambios()
    {
        var tenant = TenantActual;
        var ajenos = ChangeTracker.Entries<EntidadTenant>()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Entity.TenantId != tenant);

        if (ajenos)
        {
            throw new InvalidOperationException("No se pueden guardar cambios de otra institución.");
        }
    }
}
