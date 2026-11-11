using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Renovación y cierre de la sesión (API-AUTH-REFRESH y API-AUTH-LOGOUT, HU-012) y cambio de la
/// propia contraseña (API-AUTH-PASSWORD, HU-013).
/// </summary>
public interface IServicioSesion
{
    Task<ResultadoRenovacion> RenovarAsync(CancellationToken cancellationToken);

    /// <summary>Revoca el token en la API. Si no hay conexión no falla: la sesión se cierra igual en el cliente.</summary>
    Task CerrarAsync(string motivo, CancellationToken cancellationToken);

    /// <summary>PUT auth/password. Lanza <see cref="ErrorApi"/> con los errores por campo («actual», «nueva»).</summary>
    Task CambiarContrasenaAsync(string actual, string nueva, CancellationToken cancellationToken);
}

/// <summary>Sesión nueva, rechazo de la API (401) o falta de conexión (se reintenta en la siguiente revisión).</summary>
public sealed record ResultadoRenovacion(SesionUsuario? Sesion, bool Rechazada)
{
    public static ResultadoRenovacion SinConexion { get; } = new(null, false);
}

public sealed class ServicioSesionHttp(ClienteApi cliente) : IServicioSesion
{
    public const string MotivoVoluntario = "voluntario";
    public const string MotivoInactividad = "inactividad";

    private sealed record RespuestaSesion(string Token, DateTimeOffset ExpiraEn, string Rol, bool DebeCambiarClave);

    public async Task<ResultadoRenovacion> RenovarAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var respuesta = await cliente.Http.PostAsync("auth/refresh", null, cancellationToken);
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ResultadoRenovacion(null, Rechazada: true);
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return ResultadoRenovacion.SinConexion;
            }

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaSesion>(cancellationToken);
            return cuerpo is null
                ? ResultadoRenovacion.SinConexion
                : new ResultadoRenovacion(new SesionUsuario(cuerpo.Token, cuerpo.ExpiraEn, cuerpo.Rol, cuerpo.DebeCambiarClave), false);
        }
        catch (Exception error) when (error is HttpRequestException || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return ResultadoRenovacion.SinConexion;
        }
    }

    public async Task CerrarAsync(string motivo, CancellationToken cancellationToken)
    {
        try
        {
            using var respuesta = await cliente.Http.PostAsJsonAsync("auth/logout", new { motivo }, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Sin conexión: el token deja de usarse en el cliente y expira solo (máximo 30 minutos).
        }
    }

    public Task CambiarContrasenaAsync(string actual, string nueva, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync(
            cliente,
            HttpMethod.Put,
            "auth/password",
            new { actual, nueva },
            estado => estado == 401 ? "Tu sesión ya no es válida. Inicia sesión de nuevo." : null,
            cancellationToken);
}
