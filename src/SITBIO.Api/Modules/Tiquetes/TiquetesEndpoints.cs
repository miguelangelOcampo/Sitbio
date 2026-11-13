using Sitbio.Application.Tiquetes;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Tiquetes;

/// <summary>
/// API-TIQ (DOC-06): reportan fallas el Ingeniero Biomédico (AC-02) y el Personal Clínico (AC-04);
/// consultan AC-02, AC-03 y AC-04, y AC-04 solo ve los tiquetes que reportó. Sin token responde 401;
/// con otro rol, 403. El permiso se valida aquí, no en WPF (RNF-003).
/// </summary>
public static class TiquetesEndpoints
{
    public const string PoliticaReporte = "ReporteTiquetes";
    public const string PoliticaConsulta = "ConsultaTiquetes";

    public static IServiceCollection AddModuloTiquetes(this IServiceCollection services)
    {
        services.AddScoped<GestionTiquetes>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaReporte, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico, CodigosRol.PersonalClinico))
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento,
                CodigosRol.PersonalClinico));
        return services;
    }

    public static RouteGroupBuilder MapTiquetes(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/tiquetes");

        grupo.MapGet("/", async (
                GestionTiquetes caso,
                CancellationToken cancellationToken,
                string? estado = null,
                string? criticidad = null,
                int page = 1,
                int pageSize = 20) =>
                Results.Ok(await caso.ListarAsync(new FiltroTiquetes(estado, criticidad, page, pageSize), cancellationToken)))
            .WithName("ListarTiquetes")
            .RequireAuthorization(PoliticaConsulta);

        // Catálogo para el formulario UI-009: quien reporta no tiene acceso a /catalogos.
        grupo.MapGet("/tipos-falla", async (GestionTiquetes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.TiposFallaAsync(cancellationToken)))
            .WithName("TiposFallaParaReportar")
            .RequireAuthorization(PoliticaReporte);

        grupo.MapGet("/{codigo}", async (string codigo, GestionTiquetes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(codigo, cancellationToken)))
            .WithName("ObtenerTiquete")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPost("/", async (DatosTiquete datos, GestionTiquetes caso, CancellationToken cancellationToken) =>
            {
                var creado = await caso.ReportarAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/tiquetes/{creado.Codigo}", creado);
            })
            .WithName("ReportarFalla")
            .RequireAuthorization(PoliticaReporte);

        return api;
    }
}
