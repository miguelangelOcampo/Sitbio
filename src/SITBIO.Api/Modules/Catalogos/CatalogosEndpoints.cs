using Sitbio.Application.Catalogos;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Catalogos;

/// <summary>
/// API-CAT (DOC-06), por ahora solo la consulta: GET /catalogos/{tipo} para AC-01 y AC-02, que
/// alimenta el formulario de equipos (UI-007). Crear y editar (POST/PUT) llegan con HU-025.
/// Sin token responde 401; con otro rol, 403; un tipo de catálogo desconocido, 404.
/// </summary>
public static class CatalogosEndpoints
{
    public const string PoliticaConsulta = "ConsultaCatalogos";

    public static IServiceCollection AddModuloCatalogos(this IServiceCollection services)
    {
        services.AddScoped<ConsultaCatalogos>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico));
        return services;
    }

    public static RouteGroupBuilder MapCatalogos(this RouteGroupBuilder api)
    {
        api.MapGroup("/catalogos").MapGet("/{tipo}", async (string tipo, ConsultaCatalogos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ListarAsync(tipo, cancellationToken)))
            .WithName("ListarCatalogo")
            .RequireAuthorization(PoliticaConsulta);
        return api;
    }
}
