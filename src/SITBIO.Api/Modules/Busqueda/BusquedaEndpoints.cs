using Sitbio.Application.Busqueda;

namespace Sitbio.Api.Modules.Busqueda;

/// <summary>
/// Búsqueda global (D-04): GET /busqueda?q= devuelve equipos, tiquetes y órdenes agrupados, máximo cinco por
/// grupo, de la institución del token. La usan los roles con consulta de algún módulo; la Directiva (AC-06)
/// recibe 403. Sin token responde 401. Lo que ve cada rol en cada grupo lo decide <see cref="BusquedaGlobal"/>.
/// </summary>
public static class BusquedaEndpoints
{
    public const string PoliticaBusqueda = "BusquedaGlobal";

    public static IServiceCollection AddModuloBusqueda(this IServiceCollection services)
    {
        services.AddScoped<BusquedaGlobal>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaBusqueda, politica => politica.RequireRole(BusquedaGlobal.Roles));
        return services;
    }

    public static RouteGroupBuilder MapBusqueda(this RouteGroupBuilder api)
    {
        api.MapGet("/busqueda", async (BusquedaGlobal caso, CancellationToken cancellationToken, string? q = null) =>
                Results.Ok(await caso.BuscarAsync(q, cancellationToken)))
            .WithName("BusquedaGlobal")
            .RequireAuthorization(PoliticaBusqueda);
        return api;
    }
}
