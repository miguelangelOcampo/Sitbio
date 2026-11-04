using System.Text;
using System.Text.Json;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Sesión abierta en el cliente. Solo vive en memoria: el token no se guarda en disco
/// (estación compartida, SEC-002) y se descarta al cerrar la aplicación.
/// </summary>
public sealed class SesionActual
{
    public SesionUsuario? Sesion { get; private set; }

    public bool Abierta => Sesion is not null;

    /// <summary>
    /// Usuario de la sesión (claim «sub» del token). Solo sirve para la interfaz, por ejemplo
    /// marcar «(tú)»; la API valida el token en cada solicitud.
    /// </summary>
    public Guid? UsuarioId => Sesion is null ? null : LeerUsuario(Sesion.Token);

    /// <summary>La API rechazó el token de la sesión abierta (401): revocado o vencido.</summary>
    public event EventHandler? Rechazada;

    public void Iniciar(SesionUsuario sesion) => Sesion = sesion;

    public void Cerrar() => Sesion = null;

    /// <summary>Avisa que la API rechazó el token; puede llamarse desde cualquier hilo.</summary>
    public void NotificarRechazo()
    {
        if (Abierta)
        {
            Rechazada?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Guid? LeerUsuario(string token)
    {
        var partes = token.Split('.');
        if (partes.Length != 3)
        {
            return null;
        }

        try
        {
            var base64 = partes[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            using var carga = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            return carga.RootElement.TryGetProperty("sub", out var sub) && Guid.TryParse(sub.GetString(), out var id) ? id : null;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return null;
        }
    }
}
