using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>Las dos versiones de una foto ya revisadas: mismo formato, miniatura de 128 px y vista de hasta 512 px.</summary>
public sealed record FotoRevisada(byte[] Miniatura, byte[] Vista, string TipoMime)
{
    public string Extension => FotografiaEquipo.Extension(TipoMime);

    public long TamanoBytes => (long)Miniatura.Length + Vista.Length;
}

/// <summary>
/// Reglas de imagen comunes a la foto del equipo (RF-031) y a la foto de perfil (J-11): miniatura de 128 × 128 px y
/// vista de hasta 512 px en el lado mayor, JPG, PNG o WebP del mismo formato y hasta 2 MB cada una.
/// </summary>
public static class RevisionFoto
{
    /// <summary>
    /// Lee y revisa las dos versiones; cada problema queda en <paramref name="errores"/>. Devuelve null si hay algún
    /// error, también los que el llamador ya traía (por ejemplo, el aviso de datos personales sin aceptar).
    /// </summary>
    public static async Task<FotoRevisada?> RevisarAsync(
        VersionFoto? miniatura, VersionFoto? vista, Dictionary<string, string[]> errores, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errores);
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

        if (errores.Count > 0 || bytesMiniatura is null || bytesVista is null || imagenVista is not { } imagen)
        {
            return null;
        }

        return new FotoRevisada(bytesMiniatura, bytesVista, imagen.TipoMime);
    }

    /// <summary>
    /// Guarda la vista y la miniatura en la carpeta indicada y ejecuta <paramref name="confirmar"/> con la ruta de la
    /// vista (registrar la fila). Si algo falla, borra los archivos recién guardados y deja pasar el error.
    /// </summary>
    public static async Task<string> GuardarAsync(
        IAlmacenDocumentos almacen,
        Guid tenantId,
        Guid carpeta,
        string nombreVista,
        FotoRevisada foto,
        Func<string, Task> confirmar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(almacen);
        ArgumentNullException.ThrowIfNull(foto);
        ArgumentNullException.ThrowIfNull(confirmar);
        var rutaVista = await almacen.GuardarAsync(tenantId, carpeta, nombreVista, new MemoryStream(foto.Vista), cancellationToken);
        string? rutaMiniatura = null;
        try
        {
            rutaMiniatura = await almacen.GuardarAsync(
                tenantId, carpeta, FotografiaEquipo.RutaMiniatura(nombreVista), new MemoryStream(foto.Miniatura), cancellationToken);
            await confirmar(rutaVista);
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

        return rutaVista;
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
}
