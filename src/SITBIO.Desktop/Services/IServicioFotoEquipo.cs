using System.Net.Http;
using System.Net.Http.Headers;

namespace Sitbio.Desktop.Services;

/// <summary>Respuesta de PUT /equipos/{id}/foto (FotoEquipoDto).</summary>
public sealed record FotoEquipo(Guid DocumentoId, Guid EquipoId, string TipoMime, long TamanoBytes, DateTimeOffset FechaCarga);

/// <summary>
/// Fotografía del equipo (RF-031, API-EQ-FOTO). Todos los roles que consultan la ven; solo AC-02 la agrega,
/// cambia o quita (la API lo valida, RNF-003). El cliente envía la imagen ya reducida: el original nunca sale del equipo.
/// </summary>
public interface IServicioFotoEquipo
{
    /// <summary>Miniatura (128) o vista (512); null si el equipo no tiene foto o no se pudo traer.</summary>
    Task<byte[]?> DescargarAsync(Guid equipoId, int lado, CancellationToken cancellationToken);

    /// <summary>Reemplaza la foto. Enviarla implica que el usuario aceptó el aviso de datos personales (Ley 1581).</summary>
    Task<FotoEquipo> GuardarAsync(Guid equipoId, FotoReducida foto, CancellationToken cancellationToken);

    Task QuitarAsync(Guid equipoId, CancellationToken cancellationToken);
}

public sealed class ServicioFotoEquipoHttp(ClienteApi cliente) : IServicioFotoEquipo
{
    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite cambiar la fotografía del equipo. Solo el Ingeniero Biomédico puede hacerlo.",
        404 => "El equipo no existe, no pertenece a tu institución o no tiene fotografía.",
        _ => null,
    };

    public async Task<byte[]?> DescargarAsync(Guid equipoId, int lado, CancellationToken cancellationToken)
    {
        try
        {
            var archivo = await PeticionApi.DescargarAsync(cliente, $"equipos/{equipoId}/foto?tam={lado}", Mensaje, cancellationToken);
            return archivo.Contenido.Length > 0 ? archivo.Contenido : null;
        }
        catch (ErrorApi)
        {
            // Sin foto, sin conexión o sin permiso: se muestra el ícono del tipo de equipo.
            return null;
        }
    }

    public Task<FotoEquipo> GuardarAsync(Guid equipoId, FotoReducida foto, CancellationToken cancellationToken)
    {
        var formulario = new MultipartFormDataContent { { new StringContent("true"), "avisoAceptado" } };
        Agregar(formulario, "miniatura", foto.Miniatura, foto.TipoMime, "miniatura" + foto.Extension);
        Agregar(formulario, "vista", foto.Vista, foto.TipoMime, "vista" + foto.Extension);
        return PeticionApi.EnviarAsync<FotoEquipo>(cliente, HttpMethod.Put, $"equipos/{equipoId}/foto", formulario, Mensaje, cancellationToken);
    }

    public Task QuitarAsync(Guid equipoId, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync(cliente, HttpMethod.Delete, $"equipos/{equipoId}/foto", null, Mensaje, cancellationToken);

    private static void Agregar(MultipartFormDataContent formulario, string campo, byte[] contenido, string tipoMime, string nombre)
    {
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new MediaTypeHeaderValue(tipoMime);
        formulario.Add(parte, campo, nombre);
    }
}
