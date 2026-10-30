using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sitbio.Desktop.Services;

/// <summary>
/// POST auth/login. Traduce las respuestas de la API (Problem Details con «codigo») a
/// <see cref="FalloLogin"/>; nunca muestra al usuario el detalle técnico.
/// </summary>
public sealed class ServicioAutenticacionHttp(HttpClient http) : IServicioAutenticacion
{
    private sealed record RespuestaLogin(string Token, DateTimeOffset ExpiraEn, string Rol, bool DebeCambiarClave);

    public async Task<ResultadoLogin> IniciarSesionAsync(string correo, string contrasena, CancellationToken cancellationToken)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.PostAsJsonAsync("auth/login", new { correo, contrasena }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ResultadoLogin.Fallido(FalloLogin.SinConexion);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Tiempo de espera agotado
            return ResultadoLogin.Fallido(FalloLogin.SinConexion);
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaLogin>(cancellationToken);
                return cuerpo is null
                    ? ResultadoLogin.Fallido(FalloLogin.Inesperado)
                    : ResultadoLogin.Exitoso(new SesionUsuario(cuerpo.Token, cuerpo.ExpiraEn, cuerpo.Rol, cuerpo.DebeCambiarClave));
            }

            return respuesta.StatusCode switch
            {
                HttpStatusCode.Unauthorized => ResultadoLogin.Fallido(
                    await LeerCodigoAsync(respuesta, cancellationToken) == "MSG-002" ? FalloLogin.AccesoNoDisponible : FalloLogin.CredencialesIncorrectas),
                HttpStatusCode.TooManyRequests => ResultadoLogin.Fallido(FalloLogin.DemasiadosIntentos),
                _ => ResultadoLogin.Fallido(FalloLogin.Inesperado),
            };
        }
    }

    private static async Task<string?> LeerCodigoAsync(HttpResponseMessage respuesta, CancellationToken cancellationToken)
    {
        try
        {
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancellationToken));
            return documento.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
