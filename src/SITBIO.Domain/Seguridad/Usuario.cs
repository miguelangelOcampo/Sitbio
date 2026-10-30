using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Usuario de una institución (tabla <c>usuario</c>). El correo es único dentro
/// del tenant, por eso se guarda normalizado. Datos personales protegidos por la
/// Ley 1581 de 2012: no se escriben en los registros.
/// </summary>
public sealed class Usuario : EntidadTenant
{
    // Constructor para EF Core.
    private Usuario(Guid tenantId)
        : base(tenantId)
    {
    }

    public Usuario(Guid tenantId, int rolId, string nombres, string apellidos, string correo, string contrasenaHash, string? telefono = null)
        : base(tenantId)
    {
        RolId = rolId;
        Nombres = Requerido(nombres, nameof(nombres));
        Apellidos = Requerido(apellidos, nameof(apellidos));
        Correo = NormalizarCorreo(correo);
        ContrasenaHash = Requerido(contrasenaHash, nameof(contrasenaHash));
        Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
        EstadoActivo = true;
        FechaCreacion = DateTimeOffset.UtcNow;
    }

    public int RolId { get; private set; }

    public Rol? Rol { get; private set; }

    public string Nombres { get; private set; } = string.Empty;

    public string Apellidos { get; private set; } = string.Empty;

    public string Correo { get; private set; } = string.Empty;

    public string ContrasenaHash { get; private set; } = string.Empty;

    public string? Telefono { get; private set; }

    public short IntentosFallidos { get; private set; }

    public bool EstadoBloqueo { get; private set; }

    public bool CambioContrasenaObligatorio { get; private set; }

    public bool EstadoActivo { get; private set; }

    public DateTimeOffset? UltimoAcceso { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    /// <summary>RN-006: la cuenta se bloquea al quinto intento fallido consecutivo.</summary>
    public const short MaximoIntentosFallidos = 5;

    public bool PuedeIniciarSesion => EstadoActivo && !EstadoBloqueo;

    /// <summary>Suma un intento fallido y bloquea la cuenta al llegar al máximo (RN-006).</summary>
    public void RegistrarIntentoFallido()
    {
        if (EstadoBloqueo)
        {
            return;
        }

        IntentosFallidos++;
        if (IntentosFallidos >= MaximoIntentosFallidos)
        {
            EstadoBloqueo = true;
        }
    }

    /// <summary>Un acceso correcto reinicia el conteo: los intentos de RN-006 son consecutivos.</summary>
    public void RegistrarAccesoExitoso(DateTimeOffset momento)
    {
        if (!PuedeIniciarSesion)
        {
            throw new InvalidOperationException("La cuenta no puede iniciar sesión.");
        }

        IntentosFallidos = 0;
        UltimoAcceso = momento;
    }

    /// <summary>Correo sin espacios y en minúsculas: es único en todo el sistema (V005) sin distinguir mayúsculas.</summary>
    public static string NormalizarCorreo(string correo)
    {
        var normalizado = Requerido(correo, nameof(correo)).ToLowerInvariant();
        if (!normalizado.Contains('@'))
        {
            throw new ArgumentException("El correo no es válido.", nameof(correo));
        }

        return normalizado;
    }

    private static string Requerido(string valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException($"El campo {campo} es obligatorio.", campo);
        }

        return valor.Trim();
    }
}
