using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>
/// Sección "Jwt" de la configuración (ADR-003). La clave sale de User Secrets o de la
/// variable de entorno Jwt__Clave, nunca de appsettings.json (SEC-008).
/// </summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";
    public const int MinutosMaximos = 30;
    public const int BytesMinimosClave = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Clave { get; set; } = string.Empty;

    public int MinutosVigencia { get; set; } = MinutosMaximos;

    public void Validar()
    {
        if (Encoding.UTF8.GetByteCount(Clave) < BytesMinimosClave)
        {
            throw new InvalidOperationException(
                $"La clave JWT (Jwt:Clave) debe tener al menos {BytesMinimosClave} bytes; configúrela con User Secrets o con la variable de entorno Jwt__Clave.");
        }

        if (MinutosVigencia is < 1 or > MinutosMaximos)
        {
            throw new InvalidOperationException($"La vigencia del JWT debe estar entre 1 y {MinutosMaximos} minutos (RNF-004).");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Faltan Jwt:Issuer o Jwt:Audience en la configuración.");
        }
    }

    public SymmetricSecurityKey CrearClaveFirma() => new(Encoding.UTF8.GetBytes(Clave));
}
