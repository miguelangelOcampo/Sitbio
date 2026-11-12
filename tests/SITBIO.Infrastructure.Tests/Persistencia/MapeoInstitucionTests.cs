using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Instituciones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Persistencia;

/// <summary>Columnas de <c>tenant</c> según el script 01 y la V002 (HU-024); no abre conexión.</summary>
public class MapeoInstitucionTests
{
    [Theory]
    [InlineData(nameof(Tenant.Id), "id_tenant")]
    [InlineData(nameof(Tenant.Nombre), "nombre")]
    [InlineData(nameof(Tenant.Nit), "nit")]
    [InlineData(nameof(Tenant.DiasAnticipacionAlerta), "dias_anticipacion_alerta")]
    [InlineData(nameof(Tenant.Plan), "plan")]
    [InlineData(nameof(Tenant.MaxUsuariosGestion), "max_usuarios_gestion")]
    [InlineData(nameof(Tenant.MaxEquipos), "max_equipos")]
    [InlineData(nameof(Tenant.MaxAlmacenamientoGb), "max_almacenamiento_gb")]
    [InlineData(nameof(Tenant.VigenteHasta), "vigente_hasta")]
    public void Tenant_ColumnasDelEsquema(string propiedad, string columna)
    {
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql("Host=localhost;Database=sitbio_pruebas").Options;
        using var contexto = new SitbioDbContext(options, Substitute.For<IContextoUsuario>());
        var tipo = contexto.Model.FindEntityType(typeof(Tenant))!;

        Assert.Equal(columna, tipo.FindProperty(propiedad)!.GetColumnName(StoreObjectIdentifier.Table("tenant", SitbioDbContext.Esquema)));
        Assert.Empty(tipo.GetDeclaredQueryFilters());
    }
}
