using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sitbio.Application.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Seguridad;

public static class AutenticacionExtensions
{
    /// <summary>
    /// JWT Bearer con los mismos parámetros de validación que usa el generador (ADR-003).
    /// Si falta Jwt:Clave o no es válida, la API arranca igual y solo se rechazan los tokens.
    /// </summary>
    public static IServiceCollection AddAutenticacionJwt(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpcionesJwt>().Bind(configuration.GetSection(OpcionesJwt.Seccion));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcionesJwt>>((bearer, jwt) =>
            {
                // Conserva los nombres de los claims (sub, tenant_id, rol) sin mapearlos a los de Microsoft.
                bearer.MapInboundClaims = false;
                bearer.Events = new JwtBearerEvents { OnTokenValidated = RechazarSesionRevocadaAsync };
                try
                {
                    bearer.TokenValidationParameters = ParametrosValidacionJwt.Crear(jwt.Value);
                }
                catch (InvalidOperationException error)
                {
                    // La autenticación se intenta en toda petición: sin configuración válida, las
                    // rutas anónimas siguen funcionando y todo token se rechaza (401) con el motivo en el log.
                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        IssuerSigningKeyResolver = (_, _, _, _) => throw new InvalidOperationException(error.Message),
                    };
                }
            });
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Un token firmado y sin expirar se rechaza (401) si su jti no está registrado o ya fue
    /// revocado: cierre de sesión, renovación, inactividad o cambio de clave (HU-012, HU-013,
    /// SEC-002). Si el usuario tiene clave temporal, se marca para exigir el cambio (HU-013).
    /// </summary>
    public static async Task RechazarSesionRevocadaAsync(TokenValidatedContext contexto)
    {
        var jti = contexto.Principal?.FindFirst(ClaimsSitbio.IdToken)?.Value;
        var tenant = contexto.Principal?.FindFirst(ClaimsSitbio.Tenant)?.Value;
        if (string.IsNullOrWhiteSpace(jti) || !Guid.TryParse(tenant, out var tenantId))
        {
            contexto.Fail("El token no identifica la sesión.");
            return;
        }

        var sesiones = contexto.HttpContext.RequestServices.GetRequiredService<IRepositorioSesiones>();
        switch (await sesiones.ConsultarEstadoAsync(tenantId, jti, contexto.HttpContext.RequestAborted))
        {
            case EstadoSesion.Cerrada:
                contexto.Fail("La sesión fue cerrada.");
                break;
            case EstadoSesion.ClaveTemporal:
                contexto.Principal!.Identities.First().AddClaim(new Claim(ClaveTemporal.Claim, "true"));
                break;
        }
    }
}
