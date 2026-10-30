using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Modules.Autenticacion;

/// <summary>API-AUTH-LOGIN (DOC-06): inicio de sesión con límite de intentos por IP.</summary>
public static class AutenticacionEndpoints
{
    public const string PoliticaLogin = "login";
    public const string SeccionLimite = "Autenticacion:IntentosPorMinuto";
    public const int IntentosPorMinutoPredeterminados = 10;

    public static IServiceCollection AddModuloAutenticacion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IniciarSesion>();

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

        return grupo;
    }
}
