using Sitbio.Api.Modules.Usuarios;
using Sitbio.Application.Instituciones;

namespace Sitbio.Api.Modules.Instituciones;

/// <summary>
/// API-TEN (DOC-06): datos de la propia institución y consumo del plan, solo para el Administrador
/// del Sistema (AC-01). La ruta no lleva id: el tenant sale del token (RN-008, CA-HU-024-02).
/// Sin token responde 401; con otro rol, 403 (queda en la bitácora). El permiso se valida aquí (RNF-003).
/// </summary>
public static class InstitucionEndpoints
{
    public static IServiceCollection AddModuloInstituciones(this IServiceCollection services) =>
        services.AddScoped<GestionInstitucion>();

    public static RouteGroupBuilder MapInstitucion(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/tenant/actual").RequireAuthorization(UsuariosEndpoints.PoliticaAdministrador);

        grupo.MapGet("/", async (GestionInstitucion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(cancellationToken)))
            .WithName("ObtenerInstitucion");

        grupo.MapPut("/", async (DatosInstitucion datos, GestionInstitucion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ActualizarAsync(datos, cancellationToken)))
            .WithName("ActualizarInstitucion");

        return api;
    }
}
