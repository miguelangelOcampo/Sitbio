using System.Threading.RateLimiting;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Api.Modules.Instituciones;

/// <summary>
/// API-SERV-PLANES y API-SERV-SOL (DOC-06; HU-028, RF-029). Los planes y la solicitud de demostración
/// o cotización son públicos, desde el inicio de sesión, con límite de solicitudes por IP. El cambio
/// de plan exige sesión (401) del Administrador del Sistema (403) y se valida contra el consumo (RN-009).
/// </summary>
public static class SolicitudesServicioEndpoints
{
    public const string PoliticaSolicitudes = "solicitudes-servicio";
    public const string SeccionLimite = "SolicitudesServicio:PorMinuto";
    public const int SolicitudesPorMinutoPredeterminadas = 5;

    public static IServiceCollection AddModuloSolicitudesServicio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SolicitudesServicio>();

        // J-05: aviso por correo al proveedor, en segundo plano.
        services.AddSingleton<ColaAvisosSolicitudes>();
        services.AddSingleton<IAvisoSolicitudes>(sp => sp.GetRequiredService<ColaAvisosSolicitudes>());
        services.AddHostedService<ServicioAvisosSolicitudes>();

        var limite = configuration.GetValue(SeccionLimite, SolicitudesPorMinutoPredeterminadas);
        services.AddRateLimiter(opciones => opciones.AddPolicy(PoliticaSolicitudes, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

        return services;
    }

    public static RouteGroupBuilder MapSolicitudesServicio(this RouteGroupBuilder api)
    {
        api.MapGet("/planes", () => Results.Ok(SolicitudesServicio.Planes()))
            .WithName("ConsultarPlanes")
            .AllowAnonymous();

        api.MapPost("/solicitudes-servicio", async (DatosSolicitudServicio datos, IContextoUsuario contexto, SolicitudesServicio caso, CancellationToken cancellationToken) =>
            {
                SolicitudServicioCreadaDto creada;
                if (datos.Tipo == TiposSolicitudServicio.CambioPlan)
                {
                    // Leer el rol sin token responde 401; con otro rol, 403.
                    if (!SolicitudesServicio.PuedeCambiarPlan(contexto.Rol))
                    {
                        throw new ErrorPermiso("Solo el Administrador del Sistema puede solicitar el cambio de plan.");
                    }

                    creada = await caso.SolicitarCambioPlanAsync(datos, cancellationToken);
                }
                else
                {
                    creada = await caso.SolicitarAsync(datos, cancellationToken);
                }

                return Results.Created($"{RutasApi.V1}/solicitudes-servicio/{creada.Codigo}", creada);
            })
            .WithName("SolicitarServicio")
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaSolicitudes);

        return api;
    }
}
