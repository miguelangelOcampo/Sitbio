using System.IO;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Pide dónde guardar un archivo descargado y lo escribe; devuelve la confirmación o el error para la pantalla.</summary>
public static class GuardarArchivo
{
    /// <summary>(null, null) si el usuario canceló.</summary>
    public static (Aviso? Confirmacion, Aviso? Error) Guardar(IDialogoArchivos dialogo, ArchivoDescargado archivo)
    {
        var ruta = dialogo.ElegirDestino(archivo.NombreArchivo);
        if (ruta is null)
        {
            return (null, null);
        }

        try
        {
            dialogo.Guardar(ruta, archivo.Contenido);
            return (new Aviso("Archivo guardado", ruta, null, EsError: false), null);
        }
        catch (IOException error)
        {
            return (null, new Aviso("No se pudo guardar el archivo", error.Message, null));
        }
        catch (UnauthorizedAccessException)
        {
            return (null, new Aviso("No se pudo guardar el archivo", "No tienes permiso para escribir en esa carpeta. Elige otra.", null));
        }
    }
}
