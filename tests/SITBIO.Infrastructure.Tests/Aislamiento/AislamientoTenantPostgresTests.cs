using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sitbio.Application.Comun;
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

    private SitbioDbContext CrearContexto(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        return new SitbioDbContext(options, contextoUsuario);
    }

    public async Task InitializeAsync()
    {
        await using var contexto = CrearContexto(_tenantA);
        foreach (var (tenant, ubicacion, nombre) in new[] { (_tenantA, _ubicacionA, "Urgencias A"), (_tenantB, _ubicacionB, "Urgencias B") })
        {
            var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit) VALUES ({tenant}, {"Prueba aislamiento"}, {nit})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {nombre})");
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
            // Aunque se salte el filtro, guardar un registro de otro tenant se rechaza.
            var ajena = await contextoA.Ubicaciones.IgnoreQueryFilters().SingleAsync(u => u.Id == _ubicacionB);
            contextoA.Entry(ajena).Property(u => u.Nombre).CurrentValue = "Modificada por A";

            await Assert.ThrowsAsync<InvalidOperationException>(() => contextoA.SaveChangesAsync());
        }

        await using var contextoB = CrearContexto(_tenantB);
        var propia = await contextoB.Ubicaciones.SingleAsync();
        Assert.Equal("Urgencias B", propia.Nombre);
    }
}
