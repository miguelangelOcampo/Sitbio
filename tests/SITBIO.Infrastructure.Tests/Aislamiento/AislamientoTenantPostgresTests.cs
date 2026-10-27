using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Comun;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// RN-008 / DRV-001: dos tenants en la misma base; ninguno ve ni modifica los datos
/// del otro. Usa la tabla real <c>ubicacion</c> del script 01.
/// </summary>
public class AislamientoTenantPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _ubicacionA = Guid.NewGuid();
    private readonly Guid _ubicacionB = Guid.NewGuid();

    private sealed class UbicacionDePrueba : EntidadTenant
    {
        public UbicacionDePrueba(Guid id, Guid tenantId, string nombre)
            : base(tenantId)
        {
            Id = id;
            Nombre = nombre;
        }

        public string Nombre { get; set; }
    }

    private sealed class ContextoDePrueba(DbContextOptions options, IContextoUsuario contextoUsuario)
        : SitbioDbContext(options, contextoUsuario)
    {
        public DbSet<UbicacionDePrueba> Ubicaciones => Set<UbicacionDePrueba>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var ubicacion = modelBuilder.Entity<UbicacionDePrueba>();
            ubicacion.ToTable("ubicacion");
            ubicacion.HasKey(u => u.Id);
            ubicacion.Property(u => u.Id).HasColumnName("id_ubicacion");
            ubicacion.Property(u => u.Nombre).HasColumnName("nombre");
        }
    }

    private ContextoDePrueba CrearContexto(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<ContextoDePrueba>().UseNpgsql(_cadena).Options;
        return new ContextoDePrueba(options, contextoUsuario);
    }

    public async Task InitializeAsync()
    {
        await using (var contexto = CrearContexto(_tenantA))
        {
            foreach (var tenant in new[] { _tenantA, _tenantB })
            {
                var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit) VALUES ({tenant}, {"Prueba aislamiento"}, {nit})");
            }

            contexto.Ubicaciones.Add(new UbicacionDePrueba(_ubicacionA, _tenantA, "Urgencias A"));
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = CrearContexto(_tenantB))
        {
            contexto.Ubicaciones.Add(new UbicacionDePrueba(_ubicacionB, _tenantB, "Urgencias B"));
            await contexto.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var contexto = CrearContexto(_tenantA);
        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.ubicacion WHERE tenant_id IN ({_tenantA}, {_tenantB})");
        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
    }

    [FactIntegracion(Variable)]
    public async Task Consultar_CadaTenantSoloVeSusDatos()
    {
        await using var contextoA = CrearContexto(_tenantA);
        await using var contextoB = CrearContexto(_tenantB);

        var deA = await contextoA.Ubicaciones.Select(u => u.Id).ToListAsync();
        var deB = await contextoB.Ubicaciones.Select(u => u.Id).ToListAsync();

        Assert.Equal([_ubicacionA], deA);
        Assert.Equal([_ubicacionB], deB);
    }

    [FactIntegracion(Variable)]
    public async Task BuscarPorId_RegistroDeOtroTenant_NoLoEncuentra()
    {
        await using var contextoA = CrearContexto(_tenantA);

        var ajena = await contextoA.Ubicaciones.FirstOrDefaultAsync(u => u.Id == _ubicacionB);

        Assert.Null(ajena);
    }

    [FactIntegracion(Variable)]
    public async Task Modificar_RegistroDeOtroTenant_LanzaExcepcionYNoCambiaNada()
    {
        await using (var contextoA = CrearContexto(_tenantA))
        {
            var ajena = new UbicacionDePrueba(_ubicacionB, _tenantB, "Modificada por A");
            contextoA.Ubicaciones.Update(ajena);

            await Assert.ThrowsAsync<InvalidOperationException>(() => contextoA.SaveChangesAsync());
        }

        await using var contextoB = CrearContexto(_tenantB);
        var propia = await contextoB.Ubicaciones.SingleAsync();
        Assert.Equal("Urgencias B", propia.Nombre);
    }
}
