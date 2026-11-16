using Sitbio.Application.Analitica;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Analitica;

/// <summary>
/// API-ANA (DOC-06; HU-009): los resultados K-Means los consultan AC-02, AC-05, AC-06 y AC-07, sobre su
/// institución. Sin token responde 401; con otro rol, 403 (RNF-003). Sin resultados del lote, 409 ANA-001.
/// </summary>
public static class AnaliticaEndpoints
{
    public const string PoliticaAnalitica = "Analitica";

    public static IServiceCollection AddModuloAnalitica(this IServiceCollection services)
    {
        services.AddScoped<ConsultaAnalitica>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaAnalitica, politica => politica.RequireRole(
                CodigosRol.IngenieroBiomedico,
                CodigosRol.GerenteBiomedico,
                CodigosRol.DirectivaInstitucional,
                CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapAnalitica(this RouteGroupBuilder api)
    {
        api.MapGet("/analitica/agrupamientos", async (ConsultaAnalitica caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.AgrupamientosAsync(cancellationToken)))
            .WithName("AgrupamientosAnalitica")
            .RequireAuthorization(PoliticaAnalitica);
        return api;
    }
}
