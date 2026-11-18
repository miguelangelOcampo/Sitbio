using System.Net.Http;
using System.Net.Http.Headers;

namespace Sitbio.Desktop.Services;

/// <summary>Un problema de una fila del archivo (la fila 1 es el encabezado).</summary>
public sealed record ErrorFilaImportacion(int Fila, string? Placa, string Columna, string Mensaje);

/// <summary>Respuesta de POST /equipos/importacion (API-EQ-IMP, HU-016).</summary>
public sealed record ResultadoImportacion(
    bool Confirmada,
    int TotalFilas,
    int FilasValidas,
    int CupoDisponible,
    int Importables,
    int Importadas,
    IReadOnlyList<ErrorFilaImportacion> Errores);

/// <summary>API-EQ-IMP (HU-016): solo AC-01 y AC-02 descargan la plantilla e importan.</summary>
public interface IServicioImportacion
{
    /// <summary>Plantilla en «xlsx» (con los valores permitidos de la institución) o «csv».</summary>
    Task<ArchivoDescargado> DescargarPlantillaAsync(string formato, CancellationToken cancellationToken);

    /// <summary>Vista previa (<paramref name="confirmar"/> = false) o registro de las filas válidas que caben en el plan.</summary>
    Task<ResultadoImportacion> ProcesarAsync(string nombreArchivo, byte[] contenido, bool confirmar, CancellationToken cancellationToken);
}

public sealed class ServicioImportacionHttp(ClienteApi cliente) : IServicioImportacion
{
    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite importar equipos. Solo el Administrador del Sistema y el Ingeniero Biomédico pueden hacerlo.",
        _ => null,
    };

    public Task<ArchivoDescargado> DescargarPlantillaAsync(string formato, CancellationToken cancellationToken) =>
        PeticionApi.DescargarAsync(cliente, $"equipos/importacion/plantilla?formato={Uri.EscapeDataString(formato)}", Mensaje, cancellationToken);

    public Task<ResultadoImportacion> ProcesarAsync(string nombreArchivo, byte[] contenido, bool confirmar, CancellationToken cancellationToken)
    {
        var formulario = new MultipartFormDataContent { { new StringContent(confirmar ? "true" : "false"), "confirmar" } };
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formulario.Add(archivo, "archivo", nombreArchivo);
        return PeticionApi.EnviarAsync<ResultadoImportacion>(cliente, HttpMethod.Post, "equipos/importacion", formulario, Mensaje, cancellationToken);
    }
}
