using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Usuarios;

/// <summary>
/// J-11 (HU-011; API-USR-FOTO): foto de perfil de quien está en la sesión. Cada usuario cambia, consulta y quita
/// solo la suya (el id sale del token, nunca de la ruta). Mismas reglas de imagen que la foto del equipo
/// (RF-031): miniatura de 128 × 128 px y vista de hasta 512 px, JPG, PNG o WebP del mismo formato y hasta 2 MB
/// cada una, ya reducidas por el cliente. No es un documento del equipo y no cuenta en el almacenamiento del
/// plan (RN-009); al cambiarla o quitarla se borran los archivos anteriores. Bitácora ACTUALIZAR del usuario.
/// </summary>
public sealed class GestionFotoPerfil(
    IRepositorioUsuarios repositorio,
    IAlmacenDocumentos almacen,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto)
{
    public const string Entidad = "usuario";

    /// <summary>Nombre base del archivo en disco; la carpeta es la del usuario dentro de su institución.</summary>
    public const string NombreArchivo = "perfil";

    public async Task<ArchivoDocumento> DescargarAsync(int tamano, CancellationToken cancellationToken)
    {
        if (tamano is not (FotografiaEquipo.LadoMiniatura or FotografiaEquipo.LadoMaximoVista))
        {
            throw new ErrorValidacion(new Dictionary<string, string[]> { ["tam"] = ["El tamaño debe ser 128 o 512."] });
        }

        var ruta = (await Usuario(cancellationToken)).FotoRuta ?? throw new ErrorNoEncontrado("Tu foto de perfil");
        var archivo = tamano == FotografiaEquipo.LadoMiniatura ? FotografiaEquipo.RutaMiniatura(ruta) : ruta;
        var contenido = almacen.Abrir(archivo) ?? throw new ErrorNoEncontrado("Tu foto de perfil");
        return new ArchivoDocumento(contenido, NombreArchivo + Path.GetExtension(ruta), TipoMime(ruta));
    }

    /// <summary>Guarda o reemplaza la foto; los archivos de la anterior se borran.</summary>
    public async Task GuardarAsync(VersionFoto? miniatura, VersionFoto? vista, CancellationToken cancellationToken)
    {
        var usuario = await Usuario(cancellationToken);
        var errores = new Dictionary<string, string[]>();
        var bytesMiniatura = await Leer(miniatura, "miniatura", errores, cancellationToken);
        var bytesVista = await Leer(vista, "vista", errores, cancellationToken);
        var imagenMiniatura = Revisar(
            bytesMiniatura,
            "miniatura",
            errores,
            i => i.Ancho == FotografiaEquipo.LadoMiniatura && i.Alto == FotografiaEquipo.LadoMiniatura,
            "La miniatura debe medir 128 × 128 px.");
        var imagenVista = Revisar(
            bytesVista,
            "vista",
            errores,
            i => i.LadoMayor <= FotografiaEquipo.LadoMaximoVista,
            "La vista no debe pasar de 512 px en el lado mayor; reduce la imagen antes de subirla.");
        if (imagenMiniatura is { } m && imagenVista is { } v && m.TipoMime != v.TipoMime)
        {
            errores["miniatura"] = ["La miniatura y la vista deben tener el mismo formato."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var anterior = usuario.FotoRuta;
        var nombreVista = $"{NombreArchivo}-{Guid.NewGuid():N}{FotografiaEquipo.Extension(imagenVista!.Value.TipoMime)}";
        var rutaVista = await almacen.GuardarAsync(contexto.TenantId, usuario.Id, nombreVista, new MemoryStream(bytesVista!), cancellationToken);
        string? rutaMiniatura = null;
        try
        {
            rutaMiniatura = await almacen.GuardarAsync(
                contexto.TenantId, usuario.Id, FotografiaEquipo.RutaMiniatura(nombreVista), new MemoryStream(bytesMiniatura!), cancellationToken);
            usuario.CambiarFoto(rutaVista);
            await repositorio.GuardarAsync(cancellationToken);
        }
        catch
        {
            almacen.Eliminar(rutaVista);
            if (rutaMiniatura is not null)
            {
                almacen.Eliminar(rutaMiniatura);
            }

            throw;
        }

        Borrar(anterior);
        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Actualizar, Entidad, usuario.Id.ToString(), Detalle("cambiada"), cancellationToken);
    }

    /// <summary>Quita la foto: se vuelven a ver las iniciales.</summary>
    public async Task QuitarAsync(CancellationToken cancellationToken)
    {
        var usuario = await Usuario(cancellationToken);
        var anterior = usuario.FotoRuta ?? throw new ErrorNoEncontrado("Tu foto de perfil");
        usuario.QuitarFoto();
        await repositorio.GuardarAsync(cancellationToken);
        Borrar(anterior);
        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Actualizar, Entidad, usuario.Id.ToString(), Detalle("retirada"), cancellationToken);
    }

    /// <summary>Tipo MIME según la extensión con que se guardó.</summary>
    public static string TipoMime(string ruta) => Path.GetExtension(ruta).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    private async Task<Domain.Seguridad.Usuario> Usuario(CancellationToken cancellationToken) =>
        (await repositorio.ObtenerAsync(contexto.UsuarioId, cancellationToken))?.Usuario ?? throw new ErrorNoEncontrado("El usuario");

    private void Borrar(string? ruta)
    {
        if (ruta is not null)
        {
            almacen.Eliminar(ruta);
            almacen.Eliminar(FotografiaEquipo.RutaMiniatura(ruta));
        }
    }

    private static async Task<byte[]?> Leer(VersionFoto? version, string campo, Dictionary<string, string[]> errores, CancellationToken cancellationToken)
    {
        if (version is null || version.TamanoBytes <= 0)
        {
            errores[campo] = [campo == "miniatura" ? "Falta la miniatura de 128 px." : "Falta la vista de 512 px."];
            return null;
        }

        if (version.TamanoBytes > FotografiaEquipo.TamanoMaximoBytes)
        {
            errores[campo] = ["La imagen reducida supera 2 MB."];
            return null;
        }

        using var memoria = new MemoryStream();
        await version.Contenido.CopyToAsync(memoria, cancellationToken);
        return memoria.ToArray();
    }

    private static ImagenFoto? Revisar(byte[]? bytes, string campo, Dictionary<string, string[]> errores, Func<ImagenFoto, bool> cumple, string mensaje)
    {
        if (bytes is null)
        {
            return null;
        }

        if (FotografiaEquipo.Leer(bytes) is not { } imagen)
        {
            errores[campo] = [GestionFotoEquipo.FormatoNoPermitido];
            return null;
        }

        if (!cumple(imagen))
        {
            errores[campo] = [mensaje];
            return null;
        }

        return imagen;
    }

    private static Dictionary<string, string> Detalle(string cambio) => new() { ["foto_perfil"] = cambio };
}
