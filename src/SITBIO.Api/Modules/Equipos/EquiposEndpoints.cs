using Sitbio.Application.Equipos;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Equipos;

/// <summary>
/// API-EQ-LIST y API-EQ (DOC-06): consulta del inventario para AC-01 a AC-05 y AC-07; registro y
/// edición solo para el Ingeniero Biomédico (AC-02). Sin token responde 401; con otro rol, 403.
/// El permiso se valida aquí, no en WPF (RNF-003). El cambio de estado, la baja lógica y su
/// reversión (PATCH /estado, HU-014) son solo de AC-02; el historial lo consultan los mismos roles.
/// </summary>
public static class EquiposEndpoints
{
    public const string PoliticaConsulta = "ConsultaEquipos";
    public const string PoliticaGestion = "GestionEquipos";

    public static IServiceCollection AddModuloEquipos(this IServiceCollection services)
    {
        services.AddScoped<GestionEquipos>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento,
                CodigosRol.PersonalClinico,
                CodigosRol.GerenteBiomedico,
                CodigosRol.EnteRegulador))
            .AddPolicy(PoliticaGestion, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico));
        return services;
    }

    public static RouteGroupBuilder MapEquipos(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/equipos");

        grupo.MapGet("/", async (
                GestionEquipos caso,
                CancellationToken cancellationToken,
                string? placa = null,
                string? serie = null,
                string? estado = null,
                string? clase = null,
                bool? calibracion = null,
                int page = 1,
                int pageSize = 20) =>
                Results.Ok(await caso.ListarAsync(new FiltroEquipos(placa, serie, estado, clase, calibracion, page, pageSize), cancellationToken)))
            .WithName("ListarEquipos")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapGet("/{id:guid}", async (Guid id, GestionEquipos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(id, cancellationToken)))
            .WithName("ObtenerEquipo")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPost("/", async (DatosEquipo datos, GestionEquipos caso, CancellationToken cancellationToken) =>
            {
                var creado = await caso.CrearAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/equipos/{creado.Id}", creado);
            })
            .WithName("RegistrarEquipo")
            .RequireAuthorization(PoliticaGestion);

        grupo.MapPut("/{id:guid}", async (Guid id, DatosEquipo datos, GestionEquipos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ActualizarAsync(id, datos, cancellationToken)))
            .WithName("ActualizarEquipo")
            .RequireAuthorization(PoliticaGestion);

        grupo.MapPatch("/{id:guid}/estado", async (Guid id, DatosCambioEstado datos, GestionEquipos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.CambiarEstadoAsync(id, datos, cancellationToken)))
            .WithName("CambiarEstadoEquipo")
            .RequireAuthorization(PoliticaGestion);

        grupo.MapGet("/{id:guid}/estados", async (Guid id, GestionEquipos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.HistorialAsync(id, cancellationToken)))
            .WithName("HistorialEstadosEquipo")
            .RequireAuthorization(PoliticaConsulta);

        return api;
    }
}
