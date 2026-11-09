using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Envío a la API sobre <see cref="ClienteApi"/> (lleva el token) y traducción de los errores a
/// <see cref="ErrorApi"/> sin detalles técnicos. Cada servicio pone el mensaje de 403 y 404.
/// </summary>
internal static class PeticionApi
{
    public static async Task<T> EnviarAsync<T>(
        ClienteApi cliente,
        HttpMethod metodo,
        string ruta,
        object? cuerpo,
        Func<int, string?> mensajePorEstado,
        CancellationToken cancellationToken)
    {
        using var respuesta = await SolicitarAsync(cliente, metodo, ruta, cuerpo, mensajePorEstado, cancellationToken);
        return await respuesta.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new ErrorApi(null, "La respuesta del servidor llegó vacía.", new Dictionary<string, string>());
    }

    /// <summary>Para las operaciones que responden 204 sin cuerpo.</summary>
    public static async Task EnviarAsync(
        ClienteApi cliente,
        HttpMethod metodo,
        string ruta,
        object? cuerpo,
        Func<int, string?> mensajePorEstado,
        CancellationToken cancellationToken)
    {
        using var respuesta = await SolicitarAsync(cliente, metodo, ruta, cuerpo, mensajePorEstado, cancellationToken);
    }

    /// <summary>Respuesta exitosa (que el llamador libera) o <see cref="ErrorApi"/>.</summary>
    private static async Task<HttpResponseMessage> SolicitarAsync(
        ClienteApi cliente,
        HttpMethod metodo,
        string ruta,
        object? cuerpo,
        Func<int, string?> mensajePorEstado,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage respuesta;
        try
        {
            using var solicitud = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
            respuesta = await cliente.Http.SendAsync(solicitud, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw SinConexion();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Tiempo de espera agotado
            throw SinConexion();
        }

        if (respuesta.IsSuccessStatusCode)
        {
            return respuesta;
        }

        using (respuesta)
        {
            throw await LeerErrorAsync(respuesta, mensajePorEstado, cancellationToken);
        }
    }

    private static ErrorApi SinConexion() => new(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true);

    private static async Task<ErrorApi> LeerErrorAsync(HttpResponseMessage respuesta, Func<int, string?> mensajePorEstado, CancellationToken cancellationToken)
    {
        string? codigo = null;
        string? detalle = null;
        var errores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancellationToken));
            var raiz = documento.RootElement;
            codigo = raiz.TryGetProperty("codigo", out var c) ? c.GetString() : null;
            detalle = raiz.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (raiz.TryGetProperty("errors", out var porCampo) && porCampo.ValueKind == JsonValueKind.Object)
            {
                foreach (var campo in porCampo.EnumerateObject())
                {
                    errores[campo.Name] = string.Join(" ", campo.Value.EnumerateArray().Select(m => m.GetString()));
                }
            }
        }
        catch (JsonException)
        {
            // Respuesta sin Problem Details: se usa el mensaje genérico.
        }

        var estado = (int)respuesta.StatusCode;
        var mensaje = mensajePorEstado(estado) ?? (estado >= 500
            ? "El servidor no pudo completar la operación. Intenta de nuevo en unos minutos."
            : errores.Count > 0 ? string.Join(" ", errores.Values) : detalle ?? "La operación no se pudo completar.");
        return new ErrorApi(codigo, mensaje, errores);
    }
}
