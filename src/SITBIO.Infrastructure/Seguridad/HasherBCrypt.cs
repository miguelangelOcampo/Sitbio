using BCrypt.Net;
using Sitbio.Application.Seguridad;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>Hash de contraseñas con BCrypt (ADR-003). Costo 12, por encima del mínimo de 11 del DOC-06.</summary>
public sealed class HasherBCrypt : IHasherContrasenas
{
    public const int Costo = 12;

    public string Hash(string contrasena)
    {
        if (string.IsNullOrEmpty(contrasena))
        {
            throw new ArgumentException("La contraseña es obligatoria.", nameof(contrasena));
        }

        return BCrypt.Net.BCrypt.HashPassword(contrasena, Costo);
    }

    public bool Verificar(string contrasena, string hash)
    {
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(contrasena, hash);
        }
        catch (SaltParseException)
        {
            // Un hash dañado se trata como contraseña incorrecta, sin revelar el motivo.
            return false;
        }
    }
}
