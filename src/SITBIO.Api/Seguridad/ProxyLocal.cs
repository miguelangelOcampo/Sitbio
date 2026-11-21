using Microsoft.AspNetCore.HttpOverrides;

namespace Sitbio.Api.Seguridad;

/// <summary>
/// La API detrás de un túnel o proxy en el mismo equipo (por ejemplo, Cloudflare Tunnel con <c>cloudflared</c>): todas
/// las peticiones llegan desde 127.0.0.1. Se toma la IP real de <c>X-Forwarded-For</c> solo cuando quien la envía es el
/// propio equipo (los valores por defecto de ASP.NET Core confían únicamente en la red de loopback), para que el límite
/// de intentos de inicio de sesión y la bitácora vean a cada usuario por separado. Sin proxy, nada cambia.
/// </summary>
public static class ProxyLocal
{
    public static IServiceCollection AddProxyLocal(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(opciones =>
            opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
}
