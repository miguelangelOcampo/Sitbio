using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Configuracion;

namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>
/// Documento de <c>configuracion_campos</c> con los nombres del script 02. <c>tipo_equipo</c> guarda
/// el id del tipo de equipo de PostgreSQL. Los opcionales vacíos se omiten: el validador rechaza
/// <c>null</c> donde espera texto o arreglo.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class DocumentoConfiguracionCampos : IDocumentoTenant
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("tipo_equipo")]
    public string TipoEquipo { get; set; } = string.Empty;

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("estado")]
    public string Estado { get; set; } = EstadosConfiguracion.Activa;

    [BsonElement("creado_por")]
    public string CreadoPor { get; set; } = string.Empty;

    [BsonElement("fecha_creacion")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime FechaCreacion { get; set; }

    [BsonElement("campos")]
    public List<DocumentoCampo> Campos { get; set; } = [];
}

[BsonIgnoreExtraElements]
public sealed class DocumentoCampo
{
    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("etiqueta")]
    [BsonIgnoreIfNull]
    public string? Etiqueta { get; set; }

    [BsonElement("tipo_dato")]
    public string TipoDato { get; set; } = string.Empty;

    [BsonElement("obligatorio")]
    public bool Obligatorio { get; set; }

    [BsonElement("opciones")]
    [BsonIgnoreIfNull]
    public List<string>? Opciones { get; set; }

    [BsonElement("unidad")]
    [BsonIgnoreIfNull]
    public string? Unidad { get; set; }
}

/// <summary>
/// Motor de campos dinámicos sobre MongoDB (ADR-002, HU-006). El tenant lo pone y lo filtra la base
/// <see cref="RepositorioMongoTenant{TDocumento}"/> (RN-008). El índice único
/// <c>uq_config_tenant_tipo_version</c> impide dos versiones con el mismo número. Sin réplica no hay
/// transacciones: primero se inserta la nueva versión y luego se pasan a históricas las demás; si
/// algo falla en medio, la lectura toma la versión más alta.
/// </summary>
public sealed class ConfiguracionCamposMongo(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario)
    : RepositorioMongoTenant<DocumentoConfiguracionCampos>(baseDatos, contextoUsuario, Coleccion), IRepositorioConfiguracionCampos
{
    public const string Coleccion = "configuracion_campos";

    public async Task<IReadOnlyList<VersionConfiguracion>> VersionesAsync(Guid tipoEquipoId, CancellationToken cancellationToken)
    {
        try
        {
            var documentos = await BuscarAsync(
                Filtro.Eq(d => d.TipoEquipo, tipoEquipoId.ToString()),
                new FindOptions<DocumentoConfiguracionCampos, DocumentoConfiguracionCampos>
                {
                    Sort = Builders<DocumentoConfiguracionCampos>.Sort.Descending(d => d.Version),
                },
                cancellationToken);
            return documentos.Select(Version).ToList();
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }
    }

    public async Task PublicarAsync(VersionConfiguracion nueva, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        var tipo = nueva.TipoEquipoId.ToString();
        try
        {
            await InsertarAsync(
                new DocumentoConfiguracionCampos
                {
                    TipoEquipo = tipo,
                    Version = nueva.Version,
                    Estado = EstadosConfiguracion.Activa,
                    CreadoPor = nueva.CreadoPor.ToString(),
                    FechaCreacion = nueva.FechaCreacion.UtcDateTime,
                    Campos = nueva.Campos.Select(Documento).ToList(),
                },
                cancellationToken);

            await ActualizarAsync(
                Filtro.And(
                    Filtro.Eq(d => d.TipoEquipo, tipo),
                    Filtro.Ne(d => d.Version, nueva.Version),
                    Filtro.Eq(d => d.Estado, EstadosConfiguracion.Activa)),
                Builders<DocumentoConfiguracionCampos>.Update.Set(d => d.Estado, EstadosConfiguracion.Historica),
                cancellationToken);
        }
        catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ErrorConflicto(
                ErroresConfiguracionCampos.CodigoVersionConcurrente,
                "Otra persona publicó una versión de este tipo de equipo al mismo tiempo. Recarga los campos y vuelve a publicar.");
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }
    }

    private static DocumentoCampo Documento(CampoDinamico campo) => new()
    {
        Nombre = campo.Nombre ?? string.Empty,
        Etiqueta = campo.Etiqueta,
        TipoDato = campo.TipoDato ?? string.Empty,
        Obligatorio = campo.Obligatorio,
        Opciones = campo.Opciones?.ToList(),
        Unidad = campo.Unidad,
    };

    private static VersionConfiguracion Version(DocumentoConfiguracionCampos documento) => new(
        Guid.Parse(documento.TipoEquipo),
        documento.Version,
        documento.Estado,
        Guid.TryParse(documento.CreadoPor, out var autor) ? autor : Guid.Empty,
        new DateTimeOffset(DateTime.SpecifyKind(documento.FechaCreacion, DateTimeKind.Utc)),
        documento.Campos
            .Select(c => new CampoDinamico(c.Nombre, c.Etiqueta, c.TipoDato, c.Obligatorio, c.Opciones, c.Unidad))
            .ToList());
}
