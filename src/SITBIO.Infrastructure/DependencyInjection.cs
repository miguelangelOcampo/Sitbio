using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Sitbio.Application.Seguridad;
using Sitbio.Application.Usuarios;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la persistencia. Las cadenas "ConnectionStrings:Postgres" y
    /// "ConnectionStrings:Mongo" salen de User Secrets o de variables de entorno,
    /// nunca de appsettings.json (SEC-008). "Mongo:Database" indica la base de MongoDB.
    /// </summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SitbioDbContext>(options =>
        {
            var cadena = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Postgres.");
            options.UseNpgsql(cadena);
        });

        services.AddSingleton<IMongoClient>(_ =>
        {
            var cadena = configuration.GetConnectionString("Mongo")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Mongo.");
            return new MongoClient(cadena);
        });

        services.AddSingleton(sp =>
        {
            var nombre = configuration["Mongo:Database"]
                ?? throw new InvalidOperationException("Falta el nombre de la base en Mongo:Database.");
            return sp.GetRequiredService<IMongoClient>().GetDatabase(nombre);
        });

        // Seguridad (ADR-003). OpcionesJwt se enlaza en la API junto con JwtBearer.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHasherContrasenas, HasherBCrypt>();
        services.AddSingleton<IGeneradorTokens, GeneradorJwt>();
        services.AddScoped<IRepositorioAcceso, RepositorioAcceso>();
        services.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
        services.AddSingleton<IGeneradorClaves, GeneradorClaves>();

        return services;
    }
}
