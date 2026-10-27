using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Comun;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Persistencia;

public class SitbioDbContextTests
{
    // No abre conexión: las pruebas solo construyen el modelo y el SQL.
    private const string CadenaSinServidor = "Host=localhost;Database=sitbio_pruebas";

    private sealed class EntidadDePrueba(Guid tenantId) : EntidadTenant(tenantId)
    {
    }

    private sealed class ContextoDePrueba(DbContextOptions options, IContextoUsuario contextoUsuario)
        : SitbioDbContext(options, contextoUsuario)
    {
        public DbSet<EntidadDePrueba> Entidades => Set<EntidadDePrueba>();
    }

    private static ContextoDePrueba CrearContexto(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<ContextoDePrueba>().UseNpgsql(CadenaSinServidor).Options;
        return new ContextoDePrueba(options, contextoUsuario);
    }

    [Fact]
    public void Modelo_EntidadConTenant_TieneFiltroGlobal()
    {
        using var contexto = CrearContexto(Guid.NewGuid());

        var tipo = contexto.Model.FindEntityType(typeof(EntidadDePrueba))!;

        Assert.NotEmpty(tipo.GetDeclaredQueryFilters());
        Assert.Equal("tenant_id", tipo.FindProperty(nameof(EntidadTenant.TenantId))!.GetColumnName());
        Assert.Equal(SitbioDbContext.Esquema, tipo.GetSchema());
    }

    [Fact]
    public void Consulta_UsaElTenantDelContextoDeCadaSolicitud()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var contextoA = CrearContexto(tenantA);
        using var contextoB = CrearContexto(tenantB);

        var sqlA = contextoA.Entidades.ToQueryString();
        var sqlB = contextoB.Entidades.ToQueryString();

        Assert.Contains("tenant_id", sqlA);
        Assert.Contains(tenantA.ToString(), sqlA);
        Assert.DoesNotContain(tenantB.ToString(), sqlA);
        Assert.Contains(tenantB.ToString(), sqlB);
    }

    [Fact]
    public void Consulta_IgnorarFiltro_NoAgregaTenant()
    {
        var tenant = Guid.NewGuid();
        using var contexto = CrearContexto(tenant);

        var sql = contexto.Entidades.IgnoreQueryFilters().ToQueryString();

        Assert.DoesNotContain(tenant.ToString(), sql);
    }

    [Fact]
    public async Task Guardar_EntidadDeOtroTenant_LanzaExcepcion()
    {
        using var contexto = CrearContexto(Guid.NewGuid());
        contexto.Entidades.Add(new EntidadDePrueba(Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => contexto.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => contexto.SaveChanges());
    }
}
