using Sitbio.Application.Equipos;
using Sitbio.Infrastructure.Configuracion;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>
/// Archivos de los documentos en disco (EXT-003): <c>Storage:AttachmentsPath/&lt;tenant&gt;/&lt;equipo&gt;/&lt;id&gt;.&lt;ext&gt;</c>.
/// El nombre en disco es el id del documento, nunca el nombre que trae el usuario. Una ruta relativa
/// se resuelve desde el directorio de trabajo de la API.
/// </summary>
public sealed class AlmacenDocumentosLocal(OpcionesAlmacenamiento opciones) : IAlmacenDocumentos
{
    private readonly string _raiz = Path.GetFullPath(
        string.IsNullOrWhiteSpace(opciones.AttachmentsPath)
            ? throw new InvalidOperationException("Falta la carpeta de adjuntos en Storage:AttachmentsPath.")
            : opciones.AttachmentsPath);

    public long TamanoMaximoBytes => opciones.TamanoMaximoMb * 1024L * 1024L;

    public async Task<string> GuardarAsync(Guid tenantId, Guid equipoId, string nombreEnDisco, Stream contenido, CancellationToken cancellationToken)
    {
        var relativa = Path.Combine(tenantId.ToString(), equipoId.ToString(), Path.GetFileName(nombreEnDisco));
        var completa = Resolver(relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(completa)!);
        await using (var destino = new FileStream(completa, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await contenido.CopyToAsync(destino, cancellationToken);
        }

        return relativa.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Stream? Abrir(string rutaRelativa)
    {
        var completa = Resolver(rutaRelativa);
        return File.Exists(completa) ? new FileStream(completa, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Eliminar(string rutaRelativa)
    {
        var completa = Resolver(rutaRelativa);
        if (File.Exists(completa))
        {
            File.Delete(completa);
        }
    }

    /// <summary>La ruta queda siempre dentro de la raíz del almacenamiento.</summary>
    private string Resolver(string rutaRelativa)
    {
        var completa = Path.GetFullPath(Path.Combine(_raiz, rutaRelativa));
        if (!completa.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La ruta del documento sale de la carpeta de adjuntos.");
        }

        return completa;
    }
}
