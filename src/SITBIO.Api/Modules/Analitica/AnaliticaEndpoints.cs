using Sitbio.Application.Analitica;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Analitica;

/// <summary>
/// API-ANA (DOC-06; HU-009): los resultados K-Means los consultan AC-02, AC-05, AC-06 y AC-07, sobre su
/// institución. Sin token responde 401; con otro rol, 403 (RNF-003). Sin resultados del lote, 409 ANA-001.
/// El recálculo bajo demanda (I-04) solo lo pide AC-02: POST responde 202 y el lote corre en segundo plano;
/// GET /analitica/agrupamiento da el estado y la fecha del último cálculo. Con uno en curso, 409 ANA-002.
/// </summary>
public static class AnaliticaEndpoints
{
    public const string PoliticaAnalitica = "Analitica";
    public const string PoliticaRecalculo = "RecalculoAnalitica";
    public const string RutaRecalculo = "/analitica/agrupamiento";

    public static IServiceCollection AddModuloAnalitica(this IServiceCollection services)
    {
        services.AddScoped<ConsultaAnalitica>();
        services.AddScoped<RecalculoAnalitica>();
        services.AddSingleton<CoordinadorRecalculos>();
        services.AddHostedService<ServicioRecalculoAnalitica>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaRecalculo, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico))
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
        api.MapPost(RutaRecalculo, async (RecalculoAnalitica caso, CancellationToken cancellationToken) =>
                Results.Accepted("/api/v1" + RutaRecalculo, await caso.SolicitarAsync(cancellationToken)))
            .WithName("RecalcularAnalitica")
            .RequireAuthorization(PoliticaRecalculo);
        api.MapGet(RutaRecalculo, async (RecalculoAnalitica caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.EstadoAsync(cancellationToken)))
            .WithName("EstadoRecalculoAnalitica")
            .RequireAuthorization(PoliticaAnalitica);
        return api;
    }
}
