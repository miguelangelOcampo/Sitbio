using Sitbio.Application.Mantenimiento;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Mantenimiento;

/// <summary>
/// API-PLAN, API-ALERTAS y API-CAL (DOC-06; HU-019, HU-020, HU-022). Solo el Ingeniero Biomédico (AC-02)
/// crea y modifica planes preventivos; los consultan AC-01, AC-02, AC-03, AC-05 y AC-07 (UI-015). Las
/// alertas las ven AC-02 y AC-03 (UI-016); al consultarlas se generan las órdenes preventivas cuya fecha
/// llegó. Las calibraciones las registra y corrige el Técnico (AC-03), las aprueba el Ingeniero (AC-02) y
/// las consultan los mismos roles de los planes (UI-017). Sin token responde 401; con otro rol, 403. El
/// permiso se valida aquí, no en WPF (RNF-003).
/// </summary>
public static class MantenimientoEndpoints
{
    public const string PoliticaGestionPlanes = "GestionPlanes";
    public const string PoliticaConsultaPlanes = "ConsultaPlanes";
    public const string PoliticaAlertas = "Alertas";
    public const string PoliticaRegistroCalibraciones = "RegistroCalibraciones";
    public const string PoliticaAprobacionCalibraciones = "AprobacionCalibraciones";

    public static IServiceCollection AddModuloMantenimiento(this IServiceCollection services)
    {
        services.AddScoped<GestionPlanes>();
        services.AddScoped<GestionAlertas>();
        services.AddScoped<GestionCalibraciones>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaGestionPlanes, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico))
            .AddPolicy(PoliticaConsultaPlanes, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento,
                CodigosRol.GerenteBiomedico,
                CodigosRol.EnteRegulador))
            .AddPolicy(PoliticaAlertas, politica => politica.RequireRole(
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento))
            .AddPolicy(PoliticaRegistroCalibraciones, politica => politica.RequireRole(CodigosRol.TecnicoMantenimiento))
            .AddPolicy(PoliticaAprobacionCalibraciones, politica => politica.RequireRole(CodigosRol.IngenieroBiomedico));
        return services;
    }

    public static RouteGroupBuilder MapMantenimiento(this RouteGroupBuilder api)
    {
        var planes = api.MapGroup("/planes-preventivos");

        planes.MapGet("/", async (
                GestionPlanes caso,
                CancellationToken cancellationToken,
                Guid? equipoId = null,
                string? estado = null,
                int page = 1,
                int pageSize = 20) =>
                Results.Ok(await caso.ListarAsync(new FiltroPlanes(equipoId, estado, page, pageSize), cancellationToken)))
            .WithName("ListarPlanesPreventivos")
            .RequireAuthorization(PoliticaConsultaPlanes);

        planes.MapGet("/{id:guid}", async (Guid id, GestionPlanes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(id, cancellationToken)))
            .WithName("ObtenerPlanPreventivo")
            .RequireAuthorization(PoliticaConsultaPlanes);

        planes.MapPost("/", async (DatosPlan datos, GestionPlanes caso, CancellationToken cancellationToken) =>
            {
                var creado = await caso.CrearAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/planes-preventivos/{creado.Id}", creado);
            })
            .WithName("CrearPlanPreventivo")
            .RequireAuthorization(PoliticaGestionPlanes);

        planes.MapPut("/{id:guid}", async (Guid id, DatosActualizacionPlan datos, GestionPlanes caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ActualizarAsync(id, datos, cancellationToken)))
            .WithName("ActualizarPlanPreventivo")
            .RequireAuthorization(PoliticaGestionPlanes);

        api.MapGet("/alertas", async (GestionAlertas caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ConsultarAsync(cancellationToken)))
            .WithName("ConsultarAlertas")
            .RequireAuthorization(PoliticaAlertas);

        var calibraciones = api.MapGroup("/calibraciones");

        calibraciones.MapGet("/", async (
                GestionCalibraciones caso,
                CancellationToken cancellationToken,
                Guid? equipoId = null,
                int page = 1,
                int pageSize = 20) =>
                Results.Ok(await caso.ListarAsync(new FiltroCalibraciones(equipoId, page, pageSize), cancellationToken)))
            .WithName("ListarCalibraciones")
            .RequireAuthorization(PoliticaConsultaPlanes);

        calibraciones.MapGet("/{id:guid}", async (Guid id, GestionCalibraciones caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ObtenerAsync(id, cancellationToken)))
            .WithName("ObtenerCalibracion")
            .RequireAuthorization(PoliticaConsultaPlanes);

        calibraciones.MapPost("/", async (DatosCalibracion datos, GestionCalibraciones caso, CancellationToken cancellationToken) =>
            {
                var registrada = await caso.RegistrarAsync(datos, cancellationToken);
                return Results.Created($"{RutasApi.V1}/calibraciones/{registrada.Id}", registrada);
            })
            .WithName("RegistrarCalibracion")
            .RequireAuthorization(PoliticaRegistroCalibraciones);

        calibraciones.MapPatch("/{id:guid}", async (Guid id, DatosCorreccionCalibracion datos, GestionCalibraciones caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.CorregirAsync(id, datos, cancellationToken)))
            .WithName("CorregirCalibracion")
            .RequireAuthorization(PoliticaRegistroCalibraciones);

        calibraciones.MapPatch("/{id:guid}/aprobacion", async (Guid id, GestionCalibraciones caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.AprobarAsync(id, cancellationToken)))
            .WithName("AprobarCalibracion")
            .RequireAuthorization(PoliticaAprobacionCalibraciones);

        return api;
    }
}
