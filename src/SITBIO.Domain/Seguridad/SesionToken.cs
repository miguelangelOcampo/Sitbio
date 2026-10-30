using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Sesión emitida con un JWT (tabla <c>sesion_token</c>). El <see cref="Jti"/> es el
/// identificador único del token; permite revocarlo antes de que expire (HU-012).
/// </summary>
public sealed class SesionToken : EntidadTenant
{
    public const int LongitudMaximaJti = 36;

    // Constructor para EF Core.
    private SesionToken(Guid tenantId)
        : base(tenantId)
    {
    }

    public SesionToken(Guid tenantId, Guid usuarioId, string jti, DateTimeOffset fechaEmision, DateTimeOffset fechaExpiracion)
        : base(tenantId)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La sesión debe pertenecer a un usuario.", nameof(usuarioId));
        }

        if (string.IsNullOrWhiteSpace(jti) || jti.Length > LongitudMaximaJti)
        {
            throw new ArgumentException($"El jti es obligatorio y tiene como máximo {LongitudMaximaJti} caracteres.", nameof(jti));
        }

        // Igual que ck_sesion_expiracion del script 01
        if (fechaExpiracion <= fechaEmision)
        {
            throw new ArgumentException("La expiración debe ser posterior a la emisión.", nameof(fechaExpiracion));
        }

        UsuarioId = usuarioId;
        Jti = jti;
        FechaEmision = fechaEmision;
        FechaExpiracion = fechaExpiracion;
    }

    public Guid UsuarioId { get; private set; }

    public string Jti { get; private set; } = string.Empty;

    public DateTimeOffset FechaEmision { get; private set; }

    public DateTimeOffset FechaExpiracion { get; private set; }

    public DateTimeOffset? FechaRevocacion { get; private set; }

    public string? MotivoRevocacion { get; private set; }
}
