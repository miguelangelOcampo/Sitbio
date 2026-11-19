using Sitbio.Application.Ordenes;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Ordenes;

/// <summary>
/// Historial de intervenciones (HU-005): consulta paginada de las intervenciones de la institución. Consultan el
/// Administrador (AC-01), el Ingeniero Biomédico (AC-02), el Técnico (AC-03), el Gerente (AC-05) y el Ente
/// Regulador (AC-07), según la matriz RBAC de «Historial». Sin token responde 401; con otro rol, 403. Todo se
/// filtra por el tenant del token (RN-008).
/// </summary>
public static class IntervencionesEndpoints
{
    public const string PoliticaHistorial = "HistorialIntervenciones";

    public static IServiceCollection AddModuloIntervenciones(this IServiceCollection services)
    {
        services.AddScoped<ConsultaHistorialIntervenciones>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaHistorial, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.TecnicoMantenimiento,
                CodigosRol.GerenteBiomedico,
                CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapIntervenciones(this RouteGroupBuilder api)
    {
        api.MapGet("/intervenciones", async (
                ConsultaHistorialIntervenciones caso,
                CancellationToken cancellationToken,
                string? equipo = null,
                Guid? tipoEquipoId = null,
                Guid? tecnicoId = null,
                string? tipoMantenimiento = null,
                DateOnly? desde = null,
                DateOnly? hasta = null,
                int page = 1,
                int pageSize = 20) =>
                Results.Ok(await caso.ConsultarAsync(
                    new FiltroHistorialIntervenciones(equipo, tipoEquipoId, tecnicoId, tipoMantenimiento, desde, hasta, page, pageSize),
                    cancellationToken)))
            .WithName("ListarIntervenciones")
            .RequireAuthorization(PoliticaHistorial);
        return api;
    }
}
