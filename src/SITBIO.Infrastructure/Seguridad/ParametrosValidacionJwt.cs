using Microsoft.IdentityModel.Tokens;
using Sitbio.Application.Seguridad;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>
/// Única definición de cómo se valida un token: la usan la API (JwtBearer) y las pruebas,
/// para que no puedan quedar distintas.
/// </summary>
public static class ParametrosValidacionJwt
{
    /// <summary>Tolerancia por diferencias de reloj entre la API y el cliente.</summary>
    public static readonly TimeSpan ToleranciaReloj = TimeSpan.FromSeconds(30);

    public static TokenValidationParameters Crear(OpcionesJwt opciones)
    {
        opciones.Validar();

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opciones.Issuer,
            ValidateAudience = true,
            ValidAudience = opciones.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = opciones.CrearClaveFirma(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = ToleranciaReloj,
            NameClaimType = ClaimsSitbio.Usuario,
            RoleClaimType = ClaimsSitbio.Rol,
        };
    }
}
