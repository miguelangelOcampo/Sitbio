using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la persistencia. La cadena "ConnectionStrings:Postgres" sale de User Secrets
    /// o de variables de entorno, nunca de appsettings.json (SEC-008).
    /// </summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SitbioDbContext>(options =>
        {
            var cadena = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Postgres.");
            options.UseNpgsql(cadena);
        });

        return services;
    }
}
