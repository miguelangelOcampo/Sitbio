using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>
/// Emite JWT firmados con HMAC-SHA256 y vigencia máxima de 30 minutos (ADR-003, RNF-004).
/// Claims: sub, tenant_id, rol y jti; sin datos personales.
/// </summary>
public sealed class GeneradorJwt(IOptions<OpcionesJwt> opciones, TimeProvider tiempo) : IGeneradorTokens
{
    private readonly JsonWebTokenHandler _manejador = new();

    public TokenEmitido Generar(Usuario usuario, string codigoRol)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (string.IsNullOrWhiteSpace(codigoRol))
        {
            throw new ArgumentException("El código de rol es obligatorio.", nameof(codigoRol));
        }

        var configuracion = opciones.Value;
        configuracion.Validar();

        var emision = tiempo.GetUtcNow();
        var expiracion = emision.AddMinutes(configuracion.MinutosVigencia);
        var jti = Guid.NewGuid().ToString();

        var token = _manejador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuracion.Issuer,
            Audience = configuracion.Audience,
            IssuedAt = emision.UtcDateTime,
            NotBefore = emision.UtcDateTime,
            Expires = expiracion.UtcDateTime,
            SigningCredentials = new SigningCredentials(configuracion.CrearClaveFirma(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimsSitbio.Usuario] = usuario.Id.ToString(),
                [ClaimsSitbio.Tenant] = usuario.TenantId.ToString(),
                [ClaimsSitbio.Rol] = codigoRol,
                [ClaimsSitbio.IdToken] = jti,
            },
        });

        return new TokenEmitido(token, jti, emision, expiracion);
    }
}
