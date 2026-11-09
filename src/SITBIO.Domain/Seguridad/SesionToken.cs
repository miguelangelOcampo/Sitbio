using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Sesión emitida con un JWT (tabla <c>sesion_token</c>). El <see cref="Jti"/> es el
/// identificador único del token; permite revocarlo antes de que expire (HU-012).
/// </summary>
public sealed class SesionToken : EntidadTenant
{
    public const int LongitudMaximaJti = 36;
    public const int LongitudMaximaMotivo = 200;

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

    public bool EstaRevocada => FechaRevocacion is not null;

    /// <summary>Vigente: no revocada y sin expirar en <paramref name="momento"/>.</summary>
    public bool EstaVigente(DateTimeOffset momento) => !EstaRevocada && momento < FechaExpiracion;

    /// <summary>
    /// Invalida el token antes de que expire (HU-012, SEC-002). La revocación es definitiva:
    /// si ya estaba revocada, conserva la fecha y el motivo originales y devuelve false.
    /// </summary>
    public bool Revocar(DateTimeOffset momento, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > LongitudMaximaMotivo)
        {
            throw new ArgumentException($"El motivo es obligatorio y tiene como máximo {LongitudMaximaMotivo} caracteres.", nameof(motivo));
        }

        if (EstaRevocada)
        {
            return false;
        }

        FechaRevocacion = momento;
        MotivoRevocacion = motivo;
        return true;
    }
}

/// <summary>Motivos de revocación que se guardan en <c>sesion_token.motivo_revocacion</c>.</summary>
public static class MotivosRevocacion
{
    public const string CierreDeSesion = "Cierre de sesión";
    public const string Inactividad = "Cierre por 30 minutos de inactividad (RF-011)";
    public const string Renovacion = "Renovada con un token nuevo";
    public const string AccesoNoDisponible = "Cuenta o institución no disponible";
    public const string CambioContrasena = "Contraseña cambiada desde otra sesión (HU-013)";
    public const string ClaveTemporal = "Clave temporal emitida por el Administrador (RF-012)";
}
