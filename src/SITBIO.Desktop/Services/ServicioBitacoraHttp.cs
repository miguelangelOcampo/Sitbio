using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>GET /bitacora sobre <see cref="ClienteApi"/> (lleva el token).</summary>
public sealed class ServicioBitacoraHttp(ClienteApi cliente) : IServicioBitacora
{
    public async Task<PaginaBitacora> ConsultarAsync(FiltroConsultaBitacora filtro, CancellationToken cancellationToken)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await cliente.Http.GetAsync(Ruta(filtro), cancellationToken);
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
                return await respuesta.Content.ReadFromJsonAsync<PaginaBitacora>(cancellationToken)
                    ?? throw new ErrorApi(null, "La respuesta del servidor llegó vacía.", new Dictionary<string, string>());
            }

            throw await LeerErrorAsync(respuesta, cancellationToken);
        }
    }

    /// <summary>Ruta con los filtros presentes; las fechas en ISO 8601 con su desfase.</summary>
    public static string Ruta(FiltroConsultaBitacora filtro)
    {
        var partes = new List<string>();
        if (filtro.UsuarioId is { } usuario)
        {
            partes.Add($"usuarioId={usuario}");
        }

        if (filtro.Desde is { } desde)
        {
            partes.Add($"desde={Uri.EscapeDataString(desde.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (filtro.Hasta is { } hasta)
        {
            partes.Add($"hasta={Uri.EscapeDataString(hasta.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Accion))
        {
            partes.Add($"accion={Uri.EscapeDataString(filtro.Accion)}");
        }

        partes.Add($"page={filtro.Pagina}");
        partes.Add($"pageSize={filtro.Tamano}");
        return "bitacora?" + string.Join("&", partes);
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
            403 => "Tu rol no permite consultar la bitácora de auditoría.",
            >= 500 => "El servidor no pudo completar la consulta. Intenta de nuevo en unos minutos.",
            _ => errores.Count > 0 ? string.Join(" ", errores.Values) : detalle ?? "No fue posible consultar la bitácora.",
        };
        return new ErrorApi(codigo, mensaje, errores);
    }
}
