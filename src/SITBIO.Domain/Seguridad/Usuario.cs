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

    /// <summary>Correo sin espacios y en minúsculas, para que la unicidad por tenant no dependa de mayúsculas.</summary>
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
