using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Usuarios;

/// <summary>
/// API-USR (DOC-06): gestión de usuarios exclusiva del Administrador del Sistema (RN-002).
/// Sin token responde 401; con otro rol, 403 (MSG-009). El permiso se valida aquí, no en WPF (RNF-003).
/// </summary>
public static class UsuariosEndpoints
{
    public const string PoliticaAdministrador = "SoloAdministrador";

    public sealed record CambioEstado(string Accion);

    public static IServiceCollection AddModuloUsuarios(this IServiceCollection services)
    {
        services.AddScoped<GestionUsuarios>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaAdministrador, politica => politica.RequireRole(CodigosRol.AdministradorSistema));
        return services;
    }

    public static RouteGroupBuilder MapUsuarios(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/usuarios").RequireAuthorization(PoliticaAdministrador);

        grupo.MapGet("/", async (GestionUsuarios caso, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await caso.ListarAsync(page, pageSize, cancellationToken)))
            .WithName("ListarUsuarios");

        grupo.MapGet("/{id:guid}", async (Guid id, GestionUsuarios caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(id, cancellationToken)))
            .WithName("ObtenerUsuario");

        grupo.MapPost("/", async (DatosUsuario datos, GestionUsuarios caso, CancellationToken cancellationToken) =>
            {
                var creado = await caso.CrearAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/usuarios/{creado.Usuario.Id}", creado);
            })
            .WithName("CrearUsuario");

        grupo.MapPut("/{id:guid}", async (Guid id, DatosUsuario datos, GestionUsuarios caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ActualizarAsync(id, datos, cancellationToken)))
            .WithName("ActualizarUsuario");

        grupo.MapPatch("/{id:guid}/estado", async (Guid id, CambioEstado cambio, GestionUsuarios caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.CambiarEstadoAsync(id, cambio.Accion, cancellationToken)))
            .WithName("CambiarEstadoUsuario");

        grupo.MapPost("/{id:guid}/clave-temporal", async (Guid id, GestionUsuarios caso, CancellationToken cancellationToken) =>
                Results.Ok(new { claveTemporal = await caso.AsignarClaveTemporalAsync(id, cancellationToken) }))
            .WithName("AsignarClaveTemporal");

        return api;
    }
}
