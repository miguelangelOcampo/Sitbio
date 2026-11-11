using Sitbio.Application.Catalogos;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Catalogos;

/// <summary>
/// API-CAT (DOC-06, HU-025): GET, POST y PUT sobre /catalogos/{tipo} para tipos de equipo,
/// ubicaciones, proveedores y tipos de falla. Solo AC-01 y AC-02 llegan al caso de uso; allí AC-01
/// queda limitado a las ubicaciones (CA-HU-025-03). Sin token responde 401; con otro rol o sobre
/// otro catálogo, 403; un catálogo o elemento que no existe (o de otra institución), 404. No hay
/// DELETE: los elementos se inactivan con PUT (CA-HU-025-02).
/// </summary>
public static class CatalogosEndpoints
{
    public const string PoliticaConsulta = "ConsultaCatalogos";
    public const string PoliticaGestion = "GestionCatalogos";

    public static IServiceCollection AddModuloCatalogos(this IServiceCollection services)
    {
        services.AddScoped<ConsultaCatalogos>();
        services.AddScoped<GestionCatalogos>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico))
            .AddPolicy(PoliticaGestion, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico));
        return services;
    }

    public static RouteGroupBuilder MapCatalogos(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/catalogos");

        grupo.MapGet("/{tipo}", async (string tipo, ConsultaCatalogos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ListarAsync(tipo, cancellationToken)))
            .WithName("ListarCatalogo")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapGet("/{tipo}/{id:guid}", async (string tipo, Guid id, GestionCatalogos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(tipo, id, cancellationToken)))
            .WithName("ObtenerElementoCatalogo")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPost("/{tipo}", async (string tipo, DatosCatalogo datos, GestionCatalogos caso, CancellationToken cancellationToken) =>
            {
                var creado = await caso.CrearAsync(tipo, datos, cancellationToken);
                var catalogo = TiposCatalogo.Normalizar(tipo);
                return Results.Created($"{RutasApi.V1}/catalogos/{catalogo}/{creado.Id}", creado);
            })
            .WithName("CrearElementoCatalogo")
            .RequireAuthorization(PoliticaGestion);

        grupo.MapPut("/{tipo}/{id:guid}", async (string tipo, Guid id, DatosCatalogo datos, GestionCatalogos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ActualizarAsync(tipo, id, datos, cancellationToken)))
            .WithName("ActualizarElementoCatalogo")
            .RequireAuthorization(PoliticaGestion);

        return api;
    }
}
