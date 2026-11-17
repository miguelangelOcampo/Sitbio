using System.Net.Http;
using System.Net.Http.Headers;

namespace Sitbio.Desktop.Services;

/// <summary>Documento de la hoja de vida (GET /equipos/{id}/documentos, HU-015).</summary>
public sealed record DocumentoEquipo(
    Guid Id,
    Guid EquipoId,
    string Tipo,
    string Nombre,
    string TipoMime,
    long TamanoBytes,
    DateTimeOffset FechaCarga,
    string SubidoPor);

/// <summary>API-EQ-DOC (HU-015): consultan AC-01 a AC-05 y AC-07; adjuntan y desactivan solo AC-02.</summary>
public interface IServicioDocumentosEquipo
{
    Task<IReadOnlyList<DocumentoEquipo>> ListarAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>POST multipart con el tipo y el archivo (PDF, JPG o PNG).</summary>
    Task<DocumentoEquipo> AdjuntarAsync(Guid equipoId, string tipo, string nombreArchivo, byte[] contenido, CancellationToken cancellationToken);

    Task<ArchivoDescargado> DescargarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken);

    /// <summary>Desactivación lógica: deja de mostrarse en la hoja de vida.</summary>
    Task DesactivarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken);
}

public sealed class ServicioDocumentosEquipoHttp(ClienteApi cliente) : IServicioDocumentosEquipo
{
    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite adjuntar ni desactivar documentos. Solo el Ingeniero Biomédico puede hacerlo.",
        404 => "El equipo o el documento no existe o no pertenece a tu institución.",
        _ => null,
    };

    public async Task<IReadOnlyList<DocumentoEquipo>> ListarAsync(Guid equipoId, CancellationToken cancellationToken) =>
        await PeticionApi.EnviarAsync<List<DocumentoEquipo>>(cliente, HttpMethod.Get, $"equipos/{equipoId}/documentos", null, Mensaje, cancellationToken);

    public Task<DocumentoEquipo> AdjuntarAsync(Guid equipoId, string tipo, string nombreArchivo, byte[] contenido, CancellationToken cancellationToken)
    {
        var formulario = new MultipartFormDataContent { { new StringContent(tipo), "tipo" } };
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formulario.Add(archivo, "archivo", nombreArchivo);
        return PeticionApi.EnviarAsync<DocumentoEquipo>(cliente, HttpMethod.Post, $"equipos/{equipoId}/documentos", formulario, Mensaje, cancellationToken);
    }

    public Task<ArchivoDescargado> DescargarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken) =>
        PeticionApi.DescargarAsync(cliente, $"equipos/{equipoId}/documentos/{documentoId}/archivo", Mensaje, cancellationToken);

    public Task DesactivarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync(cliente, HttpMethod.Delete, $"equipos/{equipoId}/documentos/{documentoId}", null, Mensaje, cancellationToken);
}
