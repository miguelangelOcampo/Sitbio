using System.Net.Http;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sitbio.Desktop.Services;

/// <summary>J-12: foto de perfil de quien está en la sesión (API-USR-FOTO, J-11).</summary>
public interface IServicioFotoPerfil
{
    /// <summary>Miniatura (128) o vista (512); null si no tiene foto o no se pudo traer (se ven las iniciales).</summary>
    Task<byte[]?> DescargarAsync(int lado, CancellationToken cancellationToken);

    Task GuardarAsync(FotoReducida foto, CancellationToken cancellationToken);

    Task QuitarAsync(CancellationToken cancellationToken);
}

public sealed class ServicioFotoPerfilHttp(ClienteApi cliente) : IServicioFotoPerfil
{
    private const string Ruta = "usuarios/me/foto";

    private static string? Mensaje(int estado) => estado switch
    {
        404 => "No tienes foto de perfil.",
        _ => null,
    };

    public async Task<byte[]?> DescargarAsync(int lado, CancellationToken cancellationToken)
    {
        try
        {
            var archivo = await PeticionApi.DescargarAsync(cliente, $"{Ruta}?tam={lado}", Mensaje, cancellationToken);
            return archivo.Contenido.Length > 0 ? archivo.Contenido : null;
        }
        catch (ErrorApi)
        {
            return null;
        }
    }

    public Task GuardarAsync(FotoReducida foto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(foto);
        var formulario = new MultipartFormDataContent();
        Agregar(formulario, "miniatura", foto.Miniatura, foto.TipoMime, "miniatura" + foto.Extension);
        Agregar(formulario, "vista", foto.Vista, foto.TipoMime, "vista" + foto.Extension);
        return PeticionApi.EnviarAsync(cliente, HttpMethod.Put, Ruta, formulario, Mensaje, cancellationToken);
    }

    public Task QuitarAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync(cliente, HttpMethod.Delete, Ruta, null, Mensaje, cancellationToken);

    private static void Agregar(MultipartFormDataContent formulario, string campo, byte[] contenido, string tipoMime, string nombre)
    {
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new MediaTypeHeaderValue(tipoMime);
        formulario.Add(parte, campo, nombre);
    }
}

/// <summary>
/// J-12: la foto de perfil que ven a la vez el avatar de la barra superior, el pie del menú, el menú de la cuenta y
/// «Mi perfil». Sin foto (<see cref="Miniatura"/> null) se muestran las iniciales.
/// </summary>
public sealed partial class FotoPerfilActual : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TieneFoto), nameof(SinFoto))]
    private byte[]? _miniatura;

    public bool TieneFoto => Miniatura is { Length: > 0 };

    public bool SinFoto => !TieneFoto;

    /// <summary>Trae la miniatura de quien acaba de entrar; si falla, quedan las iniciales.</summary>
    public async Task CargarAsync(IServicioFotoPerfil servicio)
    {
        ArgumentNullException.ThrowIfNull(servicio);
        Miniatura = await servicio.DescargarAsync(ReductorFotoWpf.LadoMiniatura, CancellationToken.None);
    }

    public void Limpiar() => Miniatura = null;
}
