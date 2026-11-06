using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>GET /equipos y GET /equipos/{id} sobre <see cref="ClienteApi"/> (lleva el token).</summary>
public sealed class ServicioEquiposHttp(ClienteApi cliente) : IServicioEquipos
{
    public Task<PaginaEquipos> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken) =>
        LeerAsync<PaginaEquipos>(Ruta(filtro), cancellationToken);

    public Task<Equipo> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        LeerAsync<Equipo>($"equipos/{id}", cancellationToken);

    /// <summary>Ruta con los filtros presentes; los textos van escapados.</summary>
    public static string Ruta(FiltroEquipos filtro)
    {
        var partes = new List<string>();
        void Agregar(string nombre, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                partes.Add($"{nombre}={Uri.EscapeDataString(valor.Trim())}");
            }
        }

        Agregar("placa", filtro.Placa);
        Agregar("serie", filtro.Serie);
        Agregar("estado", filtro.Estado);
        Agregar("clase", filtro.Clase);
        if (filtro.Calibracion is { } calibracion)
        {
            partes.Add($"calibracion={(calibracion ? "true" : "false")}");
        }

        partes.Add($"page={filtro.Pagina}");
        partes.Add($"pageSize={filtro.Tamano}");
        return "equipos?" + string.Join("&", partes);
    }

    private async Task<T> LeerAsync<T>(string ruta, CancellationToken cancellationToken)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await cliente.Http.GetAsync(ruta, cancellationToken);
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
            403 => "Tu rol no permite consultar el inventario de equipos.",
            404 => "El equipo no existe o no pertenece a tu institución.",
            >= 500 => "El servidor no pudo completar la consulta. Intenta de nuevo en unos minutos.",
            _ => errores.Count > 0 ? string.Join(" ", errores.Values) : detalle ?? "No fue posible consultar el inventario.",
        };
        return new ErrorApi(codigo, mensaje, errores);
    }
}
