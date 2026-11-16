using Sitbio.Application.Indicadores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Indicadores;

/// <summary>
/// API-KPI (DOC-06; HU-026): el tablero lo consultan AC-01, AC-02, AC-05, AC-06 y AC-07, sobre los datos de su
/// institución. Sin token responde 401; con otro rol, 403 (RNF-003).
/// </summary>
public static class IndicadoresEndpoints
{
    public const string PoliticaIndicadores = "Indicadores";

    public static IServiceCollection AddModuloIndicadores(this IServiceCollection services)
    {
        services.AddScoped<ConsultaIndicadores>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaIndicadores, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.GerenteBiomedico,
                CodigosRol.DirectivaInstitucional,
                CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapIndicadores(this RouteGroupBuilder api)
    {
        api.MapGet("/indicadores", async (ConsultaIndicadores caso, CancellationToken cancellationToken, DateOnly? desde = null, DateOnly? hasta = null) =>
                Results.Ok(await caso.ConsultarAsync(desde, hasta, cancellationToken)))
            .WithName("Indicadores")
            .RequireAuthorization(PoliticaIndicadores);
        return api;
    }
}
