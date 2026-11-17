using System.Globalization;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>Documento activo de la hoja de vida, con el nombre de quien lo subió.</summary>
public sealed record DocumentoDto(
    Guid Id,
    Guid EquipoId,
    string Tipo,
    string Nombre,
    string TipoMime,
    long TamanoBytes,
    DateTimeOffset FechaCarga,
    string SubidoPor);

/// <summary>Archivo para descargar: contenido, nombre original y tipo MIME.</summary>
public sealed record ArchivoDocumento(Stream Contenido, string Nombre, string TipoMime);

/// <summary>Almacenamiento usado por los documentos de la institución y máximo del plan (RN-009).</summary>
public sealed record CapacidadAlmacenamiento(string Plan, int MaximoGb, long UsadoBytes)
{
    public const long BytesPorGb = 1024L * 1024L * 1024L;

    public bool Admite(long bytes) => UsadoBytes + bytes <= MaximoGb * BytesPorGb;
}

/// <summary>Documentos de los equipos del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioDocumentos
{
    /// <summary>¿Existe el equipo en la institución? (otro tenant = no existe, SEC-003).</summary>
    Task<bool> ExisteEquipoAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>Documentos activos del equipo, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<DocumentoDto>> ListarAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>Documento activo del equipo; null si no existe, está desactivado o es de otro tenant.</summary>
    Task<DocumentoEquipo?> ObtenerAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken);

    /// <summary>¿El equipo ya tiene un documento activo con ese nombre (sin distinguir mayúsculas)?</summary>
    Task<bool> NombreEnUsoAsync(Guid equipoId, string nombreArchivo, CancellationToken cancellationToken);

    /// <summary>Nombre de quien subió el documento, para la respuesta.</summary>
    Task<string> NombreUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>Plan del tenant del token, su almacenamiento y lo que ocupan sus documentos (activos o no).</summary>
    Task<CapacidadAlmacenamiento> CapacidadAsync(CancellationToken cancellationToken);

    void Agregar(DocumentoEquipo documento);

    Task GuardarAsync(CancellationToken cancellationToken);
}

/// <summary>Archivos de los documentos (EXT-003): <c>Storage:AttachmentsPath/&lt;tenant&gt;/&lt;equipo&gt;/</c>.</summary>
public interface IAlmacenDocumentos
{
    /// <summary>Tamaño máximo por archivo (<c>Storage:TamanoMaximoMb</c>).</summary>
    long TamanoMaximoBytes { get; }

    /// <summary>Guarda el contenido y devuelve la ruta relativa a la raíz del almacenamiento.</summary>
    Task<string> GuardarAsync(Guid tenantId, Guid equipoId, string nombreEnDisco, Stream contenido, CancellationToken cancellationToken);

    /// <summary>Abre el archivo para leer; null si ya no está.</summary>
    Stream? Abrir(string rutaRelativa);

    /// <summary>Borra un archivo recién guardado cuya fila no se pudo registrar.</summary>
    void Eliminar(string rutaRelativa);
}

/// <summary>
/// API-EQ-DOC (HU-015, RF-015). Consulta: AC-01 a AC-05 y AC-07; carga y desactivación: solo AC-02.
/// Los permisos los exige la API; aquí van las reglas: PDF, JPG o PNG (CA-HU-015-01 y 02), tamaño
/// máximo por archivo, almacenamiento del plan (RN-009) y bitácora CREAR / ELIMINAR.
/// </summary>
public sealed class GestionDocumentos(
    IRepositorioDocumentos repositorio,
    IAlmacenDocumentos almacen,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "documento_equipo";
    public const string CodigoNombreRepetido = "DOC-001";
    public const string CodigoLimitePlan = GestionEquipos.CodigoLimitePlan;
    public const string FormatoNoPermitido = "Solo se aceptan PDF, JPG o PNG.";

    public async Task<IReadOnlyList<DocumentoDto>> ListarAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        await ExigirEquipo(equipoId, cancellationToken);
        return await repositorio.ListarAsync(equipoId, cancellationToken);
    }

    /// <summary>Adjunta el archivo al equipo (CA-HU-015-01) o lo rechaza con 400 si no es PDF, JPG o PNG (CA-HU-015-02).</summary>
    public async Task<DocumentoDto> SubirAsync(Guid equipoId, string? tipo, string? nombreArchivo, Stream contenido, long tamanoBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        await ExigirEquipo(equipoId, cancellationToken);

        var legible = contenido.CanSeek ? contenido : await CopiarEnMemoria(contenido, cancellationToken);
        var (tipoValido, nombre, mime) = await Validar(tipo, nombreArchivo, legible, tamanoBytes, cancellationToken);

        if (await repositorio.NombreEnUsoAsync(equipoId, nombre, cancellationToken))
        {
            throw new ErrorConflicto(CodigoNombreRepetido, "Ya hay un documento con ese nombre en este equipo.");
        }

        var capacidad = await repositorio.CapacidadAsync(cancellationToken);
        if (!capacidad.Admite(tamanoBytes))
        {
            // MSG-019 (RN-009): consultar y trabajar con lo registrado sigue permitido.
            throw new ErrorConflicto(
                CodigoLimitePlan,
                $"Tu institución alcanzó el máximo de almacenamiento del plan {capacidad.Plan} ({capacidad.MaximoGb} GB). "
                + "Puedes seguir consultando y trabajando con lo registrado; para ampliar la capacidad, comunícate con SITBIO.");
        }

        var documento = new DocumentoEquipo(contexto.TenantId, equipoId, contexto.UsuarioId, tipoValido, nombre, mime, tamanoBytes, tiempo.GetUtcNow());
        var ruta = await almacen.GuardarAsync(
            contexto.TenantId, equipoId, documento.Id.ToString("N") + FormatosDocumento.ExtensionEnDisco(mime), legible, cancellationToken);
        documento.UbicarEn(ruta);
        repositorio.Agregar(documento);
        try
        {
            await repositorio.GuardarAsync(cancellationToken);
        }
        catch
        {
            almacen.Eliminar(ruta);
            throw;
        }

        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Crear, Entidad, documento.Id.ToString(), Detalle(documento), cancellationToken);
        var subidoPor = await repositorio.NombreUsuarioAsync(contexto.UsuarioId, cancellationToken);
        return new DocumentoDto(documento.Id, equipoId, documento.TipoDocumento, documento.NombreArchivo, mime, tamanoBytes, documento.FechaCarga, subidoPor);
    }

    public async Task<ArchivoDocumento> DescargarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await repositorio.ObtenerAsync(equipoId, documentoId, cancellationToken) ?? throw new ErrorNoEncontrado("El documento");
        var contenido = almacen.Abrir(documento.RutaAlmacenamiento) ?? throw new ErrorNoEncontrado("El archivo del documento");
        return new ArchivoDocumento(contenido, documento.NombreArchivo, documento.TipoMime);
    }

    /// <summary>Desactivación lógica: deja de mostrarse en la hoja de vida; el archivo se conserva (bitácora ELIMINAR).</summary>
    public async Task DesactivarAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await repositorio.ObtenerAsync(equipoId, documentoId, cancellationToken) ?? throw new ErrorNoEncontrado("El documento");
        documento.Desactivar(contexto.UsuarioId, tiempo.GetUtcNow());
        await repositorio.GuardarAsync(cancellationToken);
        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Eliminar, Entidad, documento.Id.ToString(), Detalle(documento), cancellationToken);
    }

    private async Task ExigirEquipo(Guid equipoId, CancellationToken cancellationToken)
    {
        if (!await repositorio.ExisteEquipoAsync(equipoId, cancellationToken))
        {
            throw new ErrorNoEncontrado("El equipo");
        }
    }

    private async Task<(string Tipo, string Nombre, string Mime)> Validar(
        string? tipo, string? nombreArchivo, Stream contenido, long tamanoBytes, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, string[]>();
        var tipoLimpio = tipo?.Trim() ?? string.Empty;
        if (tipoLimpio.Length == 0)
        {
            errores["tipo"] = ["Selecciona el tipo de documento."];
        }
        else if (!TiposDocumento.Todos.Contains(tipoLimpio))
        {
            errores["tipo"] = [$"El tipo debe ser uno de: {string.Join(", ", TiposDocumento.Todos)}."];
        }

        // Solo el nombre, sin carpetas del equipo de origen.
        var nombre = Path.GetFileName(nombreArchivo?.Replace('\\', '/') ?? string.Empty).Trim();
        string? mime = null;
        if (nombre.Length == 0 || tamanoBytes <= 0)
        {
            errores["archivo"] = ["Selecciona un archivo."];
        }
        else if (nombre.Length > DocumentoEquipo.LongitudMaximaNombre)
        {
            errores["archivo"] = [$"El nombre del archivo admite hasta {DocumentoEquipo.LongitudMaximaNombre} caracteres."];
        }
        else if (tamanoBytes > almacen.TamanoMaximoBytes)
        {
            var mb = almacen.TamanoMaximoBytes / (1024d * 1024d);
            errores["archivo"] = [string.Create(CultureInfo.GetCultureInfo("es-CO"), $"El archivo supera {mb:0.##} MB.")];
        }
        else
        {
            mime = FormatosDocumento.MimeDe(nombre, await LeerInicio(contenido, cancellationToken));
            if (mime is null)
            {
                errores["archivo"] = [FormatoNoPermitido];
            }
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        return (tipoLimpio, nombre, mime!);
    }

    private static async Task<byte[]> LeerInicio(Stream contenido, CancellationToken cancellationToken)
    {
        var inicio = new byte[FormatosDocumento.BytesFirma];
        var leidos = await contenido.ReadAtLeastAsync(inicio, inicio.Length, throwOnEndOfStream: false, cancellationToken);
        contenido.Seek(0, SeekOrigin.Begin);
        return inicio[..leidos];
    }

    private static async Task<Stream> CopiarEnMemoria(Stream contenido, CancellationToken cancellationToken)
    {
        var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, cancellationToken);
        memoria.Position = 0;
        return memoria;
    }

    /// <summary>Sin el nombre del archivo: puede contener datos personales.</summary>
    private static Dictionary<string, string> Detalle(DocumentoEquipo documento) => new()
    {
        ["equipo_id"] = documento.EquipoId.ToString(),
        ["tipo"] = documento.TipoDocumento,
        ["tamano_bytes"] = documento.TamanoBytes.ToString(CultureInfo.InvariantCulture),
    };
}
