using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Persistencia;

/// <summary>Nombres de tablas y columnas según el script 01 y la V004; no abre conexión.</summary>
public class MapeoSeguridadTests
{
    private static IModel Modelo()
    {
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql("Host=localhost;Database=sitbio_pruebas").Options;
        using var contexto = new SitbioDbContext(options, Substitute.For<IContextoUsuario>());
        return contexto.Model;
    }

    private static string Columna(IEntityType tipo, string propiedad) =>
        tipo.FindProperty(propiedad)!.GetColumnName(StoreObjectIdentifier.Table(tipo.GetTableName()!, tipo.GetSchema()));

    [Theory]
    [InlineData(typeof(Rol), "rol")]
    [InlineData(typeof(Usuario), "usuario")]
    [InlineData(typeof(SesionToken), "sesion_token")]
    public void Entidad_SeMapeaASuTablaEnElEsquemaSitbio(Type entidad, string tabla)
    {
        var tipo = Modelo().FindEntityType(entidad)!;

        Assert.Equal(tabla, tipo.GetTableName());
        Assert.Equal(SitbioDbContext.Esquema, tipo.GetSchema());
    }

    [Fact]
    public void Columnas_CoincidenConElEsquema()
    {
        var modelo = Modelo();
        var rol = modelo.FindEntityType(typeof(Rol))!;
        var usuario = modelo.FindEntityType(typeof(Usuario))!;
        var sesion = modelo.FindEntityType(typeof(SesionToken))!;

        Assert.Equal("id_rol", Columna(rol, nameof(Rol.Id)));
        Assert.Equal("codigo", Columna(rol, nameof(Rol.Codigo)));
        Assert.Equal("id_usuario", Columna(usuario, nameof(Usuario.Id)));
        Assert.Equal("tenant_id", Columna(usuario, nameof(Usuario.TenantId)));
        Assert.Equal("rol_id", Columna(usuario, nameof(Usuario.RolId)));
        Assert.Equal("contrasena_hash", Columna(usuario, nameof(Usuario.ContrasenaHash)));
        Assert.Equal("cambio_contrasena_obligatorio", Columna(usuario, nameof(Usuario.CambioContrasenaObligatorio)));
        Assert.Equal("id_sesion", Columna(sesion, nameof(SesionToken.Id)));
        Assert.Equal("usuario_id", Columna(sesion, nameof(SesionToken.UsuarioId)));
        Assert.Equal("fecha_revocacion", Columna(sesion, nameof(SesionToken.FechaRevocacion)));
    }

    [Fact]
    public void FiltroPorTenant_SoloEnUsuarioYSesion()
    {
        var modelo = Modelo();

        Assert.NotEmpty(modelo.FindEntityType(typeof(Usuario))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(modelo.FindEntityType(typeof(SesionToken))!.GetDeclaredQueryFilters());
        Assert.Empty(modelo.FindEntityType(typeof(Rol))!.GetDeclaredQueryFilters());
    }
}
