using Sitbio.Application.Auditoria;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Auditoria;

/// <summary>
/// API-AUD (DOC-06): consulta de la bitácora para el Administrador del Sistema (AC-01) y el
/// Ente Regulador / Auditor (AC-07), CA-HU-010-03. La bitácora no se modifica desde la API.
/// </summary>
public static class AuditoriaEndpoints
{
    public const string PoliticaAuditoria = "PuedeAuditar";

    public static IServiceCollection AddModuloAuditoria(this IServiceCollection services)
    {
        services.AddScoped<ConsultarBitacora>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaAuditoria, politica => politica.RequireRole(CodigosRol.AdministradorSistema, CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapAuditoria(this RouteGroupBuilder api)
    {
        api.MapGet("/bitacora", async (
                ConsultarBitacora caso,
                CancellationToken cancellationToken,
                Guid? usuarioId = null,
                DateTimeOffset? desde = null,
                DateTimeOffset? hasta = null,
                string? accion = null,
                int page = 1,
                int pageSize = 50) =>
                Results.Ok(await caso.EjecutarAsync(new FiltroBitacora(usuarioId, desde, hasta, accion, page, pageSize), cancellationToken)))
            .WithName("ConsultarBitacora")
            .RequireAuthorization(PoliticaAuditoria);

        return api;
    }
}
