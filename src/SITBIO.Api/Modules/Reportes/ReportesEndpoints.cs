using Sitbio.Api.Modules.Analitica;
using Sitbio.Api.Modules.Indicadores;
using Sitbio.Application.Reportes;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Modules.Reportes;

/// <summary>
/// API-REP (DOC-06; HU-007, HU-027): el historial de órdenes en PDF o Excel lo descargan AC-01, AC-02 y AC-05;
/// la hoja de vida en PDF, además, el Ente Regulador (AC-07). Sin token responde 401; con otro rol, 403. El
/// permiso se valida aquí, no en WPF (RNF-003). Reportes e indicadores y Analítica en PDF con sus gráficos (J-03)
/// los exportan los mismos roles que consultan cada pantalla; el cuerpo trae los filtros y las imágenes PNG.
/// </summary>
public static class ReportesEndpoints
{
    public const string PoliticaReportes = "Reportes";
    public const string PoliticaHojaVida = "HojaVidaPdf";

    public static IServiceCollection AddModuloReportes(this IServiceCollection services)
    {
        services.AddScoped<ExportacionReportes>();
        services.AddScoped<ExportacionGraficos>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaReportes, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.GerenteBiomedico))
            .AddPolicy(PoliticaHojaVida, politica => politica.RequireRole(
                CodigosRol.AdministradorSistema,
                CodigosRol.IngenieroBiomedico,
                CodigosRol.GerenteBiomedico,
                CodigosRol.EnteRegulador));
        return services;
    }

    public static RouteGroupBuilder MapReportes(this RouteGroupBuilder api)
    {
        api.MapGet("/reportes/ordenes", async (
                ExportacionReportes caso,
                CancellationToken cancellationToken,
                DateOnly? desde = null,
                DateOnly? hasta = null,
                string? formato = null) =>
            {
                var archivo = await caso.HistorialOrdenesAsync(desde, hasta, formato, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
            })
            .WithName("ExportarHistorialOrdenes")
            .RequireAuthorization(PoliticaReportes);

        api.MapGet("/equipos/{id:guid}/hoja-vida.pdf", async (Guid id, ExportacionReportes caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.HojaVidaAsync(id, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
            })
            .WithName("HojaVidaPdf")
            .RequireAuthorization(PoliticaHojaVida);

        api.MapPost("/reportes/indicadores.pdf", async (SolicitudPdfIndicadores? solicitud, ExportacionGraficos caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.IndicadoresPdfAsync(solicitud, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
            })
            .WithName("IndicadoresPdf")
            .RequireAuthorization(IndicadoresEndpoints.PoliticaIndicadores);

        api.MapPost("/reportes/analitica.pdf", async (SolicitudPdfAnalitica? solicitud, ExportacionGraficos caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.AnaliticaPdfAsync(solicitud, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
            })
            .WithName("AnaliticaPdf")
            .RequireAuthorization(AnaliticaEndpoints.PoliticaAnalitica);

        return api;
    }
}
