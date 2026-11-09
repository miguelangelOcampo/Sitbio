using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Comun;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Modules.Autenticacion;

/// <summary>
/// API-AUTH-LOGIN, API-AUTH-REFRESH, API-AUTH-LOGOUT y API-AUTH-PASSWORD (DOC-06): inicio de
/// sesión con límite de intentos por IP, renovación del token, cierre de sesión con revocación
/// del jti (HU-012) y cambio de la propia contraseña (HU-013). Renovar, cerrar sesión y cambiar
/// la contraseña se permiten con clave temporal; el resto de la API no (MSG-004).
/// </summary>
public static class AutenticacionEndpoints
{
    public const string PoliticaLogin = "login";
    public const string SeccionLimite = "Autenticacion:IntentosPorMinuto";
    public const int IntentosPorMinutoPredeterminados = 10;

    public static IServiceCollection AddModuloAutenticacion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IniciarSesion>();
        services.AddScoped<RenovarSesion>();
        services.AddScoped<CerrarSesion>();
        services.AddScoped<CambiarContrasena>();

        var intentos = configuration.GetValue(SeccionLimite, IntentosPorMinutoPredeterminados);
        services.AddRateLimiter(opciones =>
        {
            opciones.AddPolicy(PoliticaLogin, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = intentos,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            opciones.OnRejected = async (contexto, cancellationToken) =>
            {
                var http = contexto.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiados intentos",
                        Detail = "Espere un minuto antes de intentar de nuevo.",
                    },
                });
            };
        });

        return services;
    }

    public static RouteGroupBuilder MapAutenticacion(this RouteGroupBuilder grupo)
    {
        grupo.MapPost("/auth/login", async (SolicitudInicioSesion solicitud, IniciarSesion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.EjecutarAsync(solicitud, cancellationToken)))
            .WithName("IniciarSesion")
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaLogin);

        grupo.MapPost("/auth/refresh", async (ClaimsPrincipal usuario, IContextoUsuario contexto, RenovarSesion caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.EjecutarAsync(contexto.UsuarioId, IdToken(usuario), cancellationToken)))
            .WithName("RenovarSesion")
            .WithMetadata(new PermiteClaveTemporalAttribute())
            .RequireAuthorization();

        grupo.MapPost("/auth/logout", async ([FromBody] SolicitudCierreSesion? solicitud, ClaimsPrincipal usuario, CerrarSesion caso, CancellationToken cancellationToken) =>
            {
                await caso.EjecutarAsync(IdToken(usuario), solicitud?.Motivo, cancellationToken);
                return Results.NoContent();
            })
            .WithName("CerrarSesion")
            .WithMetadata(new PermiteClaveTemporalAttribute())
            .RequireAuthorization();

        grupo.MapPut("/auth/password", async (SolicitudCambioContrasena solicitud, ClaimsPrincipal usuario, IContextoUsuario contexto, CambiarContrasena caso, CancellationToken cancellationToken) =>
            {
                await caso.EjecutarAsync(contexto.UsuarioId, IdToken(usuario), solicitud, cancellationToken);
                return Results.NoContent();
            })
            .WithName("CambiarContrasena")
            .WithMetadata(new PermiteClaveTemporalAttribute())
            .RequireAuthorization();

        return grupo;
    }

    // El jti sale del token ya validado, nunca del cuerpo de la solicitud.
    private static string? IdToken(ClaimsPrincipal usuario) => usuario.FindFirst(ClaimsSitbio.IdToken)?.Value;
}
