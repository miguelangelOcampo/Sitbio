using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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
}
