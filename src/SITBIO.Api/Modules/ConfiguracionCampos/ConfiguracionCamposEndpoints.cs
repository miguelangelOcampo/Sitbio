using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.ConfiguracionCampos;

/// <summary>
/// API-CFG (DOC-06, HU-006): GET y POST sobre /configuracion-campos/{tipoEquipoId}. Consultan el
/// Administrador (AC-01), el Ingeniero Biomédico (AC-02) y el Técnico (AC-03), que necesita los
/// campos para la intervención; solo AC-02 publica versiones (CA-HU-006-03). Sin token responde 401;
/// con otro rol, 403; un tipo de equipo que no existe o es de otra institución, 404 (CA-HU-006-02).
/// No hay PUT ni DELETE: cada publicación crea una versión nueva y las anteriores quedan históricas.
/// </summary>
public static class ConfiguracionCamposEndpoints
{
    public const string PoliticaConsulta = "ConsultaConfiguracionCampos";
    public const string PoliticaGestion = "GestionConfiguracionCampos";

    public static IServiceCollection AddModuloConfiguracionCampos(this IServiceCollection services)
    {
        services.AddScoped<GestionConfiguracionCampos>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento))
            .AddPolicy(PoliticaGestion, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico));
        return services;
    }

    public static RouteGroupBuilder MapConfiguracionCampos(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/configuracion-campos");

        grupo.MapGet("/{tipoEquipoId:guid}", async (Guid tipoEquipoId, GestionConfiguracionCampos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(tipoEquipoId, cancellationToken)))
            .WithName("ObtenerConfiguracionCampos")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPost("/{tipoEquipoId:guid}", async (Guid tipoEquipoId, DatosConfiguracionCampos datos, GestionConfiguracionCampos caso, CancellationToken cancellationToken) =>
            {
                var publicada = await caso.PublicarAsync(tipoEquipoId, datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/configuracion-campos/{tipoEquipoId}", publicada);
            })
            .WithName("PublicarConfiguracionCampos")
            .RequireAuthorization(PoliticaGestion);

        return api;
    }
}
