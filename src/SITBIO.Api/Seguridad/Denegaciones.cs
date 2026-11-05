using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Seguridad;

/// <summary>IP de la solicitud actual para la bitácora (DOC-06: «IP de origen y acciones en bitácora»).</summary>
public sealed class OrigenSolicitudHttp(IHttpContextAccessor accesor) : IOrigenSolicitud
{
    public string? IpOrigen => accesor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Registro de los accesos denegados (CA-HU-010-01, RNF-003): cada 403 queda en la bitácora
/// con usuario, fecha, hora y acción (método y ruta, sin parámetros de consulta).
/// </summary>
public static class Denegaciones
{
    public static async Task RegistrarAsync(HttpContext http)
    {
        var usuario = http.User.FindFirst(ClaimsSitbio.Usuario)?.Value;
        if (!Guid.TryParse(usuario, out var usuarioId) || usuarioId == Guid.Empty)
        {
            return;
        }

        var ruta = http.Request.Path.Value ?? string.Empty;
        var detalle = new Dictionary<string, string>
        {
            ["metodo"] = http.Request.Method,
            ["ruta"] = ruta,
            ["rol"] = http.User.FindFirst(ClaimsSitbio.Rol)?.Value ?? string.Empty,
        };

        var auditoria = http.RequestServices.GetRequiredService<IRegistroAuditoria>();
        await auditoria.RegistrarAsync(usuarioId, AccionesAuditoria.AccesoDenegado, Entidad(ruta), detalle: detalle, cancellationToken: http.RequestAborted);
    }

    /// <summary>Primer segmento después de /api/v1: «/api/v1/usuarios/…» → «usuarios».</summary>
    public static string Entidad(string ruta)
    {
        var resto = ruta.StartsWith(RutasApi.V1, StringComparison.OrdinalIgnoreCase) ? ruta[RutasApi.V1.Length..] : ruta;
        var segmento = resto.Trim('/').Split('/', 2)[0];
        return string.IsNullOrWhiteSpace(segmento) ? "api" : segmento;
    }
}

/// <summary>Las políticas de rol que responden 403 dejan su registro antes de responder.</summary>
public sealed class RegistroDenegacionesAutorizacion : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _predeterminado = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            await Denegaciones.RegistrarAsync(context);
        }

        await _predeterminado.HandleAsync(next, context, policy, authorizeResult);
    }
}
