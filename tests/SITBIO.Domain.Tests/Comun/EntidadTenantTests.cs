using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Tests.Comun;

public class EntidadTenantTests
{
    private sealed class EntidadDePrueba(Guid tenantId) : EntidadTenant(tenantId)
    {
    }

    [Fact]
    public void Crear_SinTenant_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => new EntidadDePrueba(Guid.Empty));
    }

    [Fact]
    public void Crear_ConTenant_ConservaElTenant()
    {
        var tenant = Guid.NewGuid();

        var entidad = new EntidadDePrueba(tenant);

        Assert.Equal(tenant, entidad.TenantId);
        Assert.NotEqual(Guid.Empty, entidad.Id);
    }
}
