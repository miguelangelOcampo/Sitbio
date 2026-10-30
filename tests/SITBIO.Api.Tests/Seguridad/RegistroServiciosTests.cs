using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Sitbio.Application.Comun;
using Sitbio.Application.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>La API registra la persistencia y la seguridad; no abre conexiones.</summary>
public class RegistroServiciosTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ServiciosDePersistenciaYSeguridad_SeResuelven()
    {
        var configurada = fabrica.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Postgres", "Host=localhost;Database=sitbio_pruebas")
            .UseSetting("ConnectionStrings:Mongo", "mongodb://localhost:27017")
            .UseSetting("Mongo:Database", "sitbio_pruebas")
            .UseSetting("Jwt:Clave", "clave-de-pruebas-con-mas-de-32-bytes!!"));

        using var alcance = configurada.Services.CreateScope();
        var servicios = alcance.ServiceProvider;

        Assert.NotNull(servicios.GetRequiredService<IContextoUsuario>());
        Assert.NotNull(servicios.GetRequiredService<SitbioDbContext>());
        Assert.NotNull(servicios.GetRequiredService<IMongoDatabase>());
        Assert.NotNull(servicios.GetRequiredService<IHasherContrasenas>());
        Assert.NotNull(servicios.GetRequiredService<IGeneradorTokens>());
    }
}
