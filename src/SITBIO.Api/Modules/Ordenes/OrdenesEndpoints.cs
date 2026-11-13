using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Ordenes;

/// <summary>
/// API-OT (DOC-06): solo el Ingeniero Biomédico (AC-02) asigna órdenes; consultan AC-01, AC-02, AC-03 y
/// el Ente Regulador (AC-07). El Técnico (AC-03) solo ve las suyas y las pide con
/// <c>?asignadas=me</c>. Sin token responde 401; con otro rol, 403. El permiso se valida aquí, no en
/// WPF (RNF-003). Solo el Técnico (AC-03) inicia, pausa, registra la intervención y cierra sus órdenes
/// (HU-005, HU-021); solo el Ingeniero Biomédico (AC-02) aprueba y firma una orden cerrada (HU-021).
/// </summary>
public static class OrdenesEndpoints
{
    public const string PoliticaAsignacion = "AsignacionOrdenes";
    public const string PoliticaConsulta = "ConsultaOrdenes";
    public const string PoliticaEjecucion = "EjecucionOrdenes";
    public const string PoliticaAprobacion = "AprobacionOrdenes";

    /// <summary>Único valor de <c>asignadas</c>: las órdenes del usuario del token.</summary>
    public const string AsignadasAMi = "me";

    public static IServiceCollection AddModuloOrdenes(this IServiceCollection services)
    {
        services.AddScoped<GestionOrdenes>();
        services.AddScoped<GestionEjecucion>();
        services.AddScoped<GestionAprobacion>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaAsignacion, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico))
            .AddPolicy(PoliticaEjecucion, politica => politica.RequireRole(CodigosRol.TecnicoMantenimiento))
            .AddPolicy(PoliticaAprobacion, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico))
            .AddPolicy(PoliticaConsulta, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento,
                CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapOrdenes(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/ordenes");

        grupo.MapGet("/", async (
                GestionOrdenes caso,
                CancellationToken cancellationToken,
                string? estado = null,
                string? asignadas = null,
                int page = 1,
                int pageSize = 20) =>
            {
                if (asignadas is not null && !string.Equals(asignadas.Trim(), AsignadasAMi, StringComparison.OrdinalIgnoreCase))
                {
                    throw ErrorValidacion.DeCampo("asignadas", "El filtro «asignadas» solo admite «me».");
                }

                return Results.Ok(await caso.ListarAsync(new FiltroOrdenes(estado, asignadas is not null, page, pageSize), cancellationToken));
            })
            .WithName("ListarOrdenes")
            .RequireAuthorization(PoliticaConsulta);

        // Panel de UI-011: técnicos activos con su carga, para quien asigna.
        grupo.MapGet("/tecnicos", async (GestionOrdenes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.TecnicosAsync(cancellationToken)))
            .WithName("ListarTecnicosAsignables")
            .RequireAuthorization(PoliticaAsignacion);

        grupo.MapGet("/{id:guid}", async (Guid id, GestionOrdenes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(id, cancellationToken)))
            .WithName("ObtenerOrden")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPost("/", async (DatosOrden datos, GestionOrdenes caso, CancellationToken cancellationToken) =>
            {
                var creada = await caso.AsignarAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/ordenes/{creada.Id}", creada);
            })
            .WithName("AsignarOrden")
            .RequireAuthorization(PoliticaAsignacion);

        // HU-005 y HU-021: el técnico asignado inicia, pausa o cierra su orden.
        grupo.MapPatch("/{id:guid}/estado", async (Guid id, DatosEstadoOrden datos, GestionEjecucion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.CambiarEstadoAsync(id, datos, cancellationToken)))
            .WithName("CambiarEstadoOrden")
            .RequireAuthorization(PoliticaEjecucion);

        grupo.MapGet("/{id:guid}/intervencion", async (Guid id, GestionEjecucion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerIntervencionAsync(id, cancellationToken)))
            .WithName("ObtenerIntervencion")
            .RequireAuthorization(PoliticaConsulta);

        grupo.MapPut("/{id:guid}/intervencion", async (Guid id, DatosIntervencion datos, GestionEjecucion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.RegistrarIntervencionAsync(id, datos, cancellationToken)))
            .WithName("RegistrarIntervencion")
            .RequireAuthorization(PoliticaEjecucion);

        // HU-021 (API-OT-APROB): el Ingeniero aprueba con su firma la orden cerrada; queda inalterable (RN-004).
        grupo.MapPost("/{id:guid}/aprobacion", async (Guid id, DatosAprobacion datos, GestionAprobacion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.AprobarAsync(id, datos, cancellationToken)))
            .WithName("AprobarOrden")
            .RequireAuthorization(PoliticaAprobacion);

        return api;
    }
}
