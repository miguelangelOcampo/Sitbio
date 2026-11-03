using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>
/// /usuarios sobre <see cref="ClienteApi"/> (lleva el token). Traduce los errores de la API a
/// <see cref="ErrorApi"/> sin mostrar detalles técnicos.
/// </summary>
public sealed class ServicioUsuariosHttp(ClienteApi cliente) : IServicioUsuarios
{
    private const int TamanoPagina = 100;

    private sealed record Pagina(List<UsuarioResumen> Items, int Page, int PageSize, int Total);

    public async Task<IReadOnlyList<UsuarioResumen>> ListarAsync(CancellationToken cancellationToken)
    {
        var usuarios = new List<UsuarioResumen>();
        for (var pagina = 1; ; pagina++)
        {
            var actual = await EnviarAsync<Pagina>(HttpMethod.Get, $"usuarios?page={pagina}&pageSize={TamanoPagina}", null, cancellationToken);
            usuarios.AddRange(actual.Items);
            if (actual.Items.Count == 0 || usuarios.Count >= actual.Total)
            {
                return usuarios;
            }
        }
    }

    public Task<UsuarioCreado> CrearAsync(DatosUsuario datos, CancellationToken cancellationToken) =>
        EnviarAsync<UsuarioCreado>(HttpMethod.Post, "usuarios", datos, cancellationToken);

    public Task<UsuarioResumen> ActualizarAsync(Guid id, DatosUsuario datos, CancellationToken cancellationToken) =>
        EnviarAsync<UsuarioResumen>(HttpMethod.Put, $"usuarios/{id}", datos, cancellationToken);

    public Task<UsuarioResumen> CambiarEstadoAsync(Guid id, string accion, CancellationToken cancellationToken) =>
        EnviarAsync<UsuarioResumen>(HttpMethod.Patch, $"usuarios/{id}/estado", new { accion }, cancellationToken);

    public async Task<string> AsignarClaveTemporalAsync(Guid id, CancellationToken cancellationToken)
    {
        using var documento = await EnviarAsync<JsonDocument>(HttpMethod.Post, $"usuarios/{id}/clave-temporal", null, cancellationToken);
        return documento.RootElement.GetProperty("claveTemporal").GetString() ?? string.Empty;
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken cancellationToken)
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

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                return await respuesta.Content.ReadFromJsonAsync<T>(cancellationToken)
                    ?? throw new ErrorApi(null, "La respuesta del servidor llegó vacía.", new Dictionary<string, string>());
            }

            throw await LeerErrorAsync(respuesta, cancellationToken);
        }
    }

    private static ErrorApi SinConexion() => new(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true);

    private static async Task<ErrorApi> LeerErrorAsync(HttpResponseMessage respuesta, CancellationToken cancellationToken)
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

        var mensaje = (int)respuesta.StatusCode switch
        {
            403 => "Tu rol no permite gestionar usuarios.",
            404 => "El usuario no existe o ya no está disponible.",
            >= 500 => "El servidor no pudo completar la operación. Intenta de nuevo en unos minutos.",
            _ => detalle ?? "No fue posible completar la operación.",
        };
        return new ErrorApi(codigo, mensaje, errores);
    }
}
