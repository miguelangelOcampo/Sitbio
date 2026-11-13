using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;

namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>
/// Documento de <c>intervenciones</c> con los nombres del script 02. Los ids de PostgreSQL van como texto.
/// Los opcionales vacíos se omiten porque el validador rechaza <c>null</c> donde espera un tipo.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class DocumentoIntervencion : IDocumentoTenant
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("id_intervencion")]
    public string IdIntervencion { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("equipo_id")]
    public string EquipoId { get; set; } = string.Empty;

    [BsonElement("orden_id")]
    public string OrdenId { get; set; } = string.Empty;

    [BsonElement("tipo_mantenimiento")]
    public string TipoMantenimiento { get; set; } = string.Empty;

    [BsonElement("fecha_intervencion")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime FechaIntervencion { get; set; }

    [BsonElement("tecnico_id")]
    public string TecnicoId { get; set; } = string.Empty;

    [BsonElement("diagnostico")]
    public string Diagnostico { get; set; } = string.Empty;

    [BsonElement("acciones_realizadas")]
    public string AccionesRealizadas { get; set; } = string.Empty;

    [BsonElement("repuestos")]
    public List<DocumentoRepuesto> Repuestos { get; set; } = [];

    [BsonElement("costo_mano_obra")]
    [BsonIgnoreIfNull]
    public double? CostoManoObra { get; set; }

    [BsonElement("costo_total")]
    public double CostoTotal { get; set; }

    [BsonElement("tiempo_atencion_min")]
    [BsonIgnoreIfNull]
    public int? TiempoAtencionMin { get; set; }

    [BsonElement("estado_equipo_salida")]
    [BsonIgnoreIfNull]
    public string? EstadoEquipoSalida { get; set; }

    [BsonElement("cerrada_firmada")]
    public bool CerradaFirmada { get; set; }

    [BsonElement("version_campos")]
    [BsonIgnoreIfNull]
    public int? VersionCampos { get; set; }

    [BsonElement("camposDinamicos")]
    public BsonDocument CamposDinamicos { get; set; } = [];

    /// <summary>Copia del equipo al firmar (HU-021); ausente mientras no esté firmada.</summary>
    [BsonElement("equipo_snapshot")]
    [BsonIgnoreIfNull]
    public DocumentoFotoEquipo? EquipoSnapshot { get; set; }
}

/// <summary><c>equipo_snapshot</c> (diccionario de datos NoSQL): el validador no lo declara; su forma la garantiza la API.</summary>
[BsonIgnoreExtraElements]
public sealed class DocumentoFotoEquipo
{
    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("marca")]
    public string Marca { get; set; } = string.Empty;

    [BsonElement("modelo")]
    public string Modelo { get; set; } = string.Empty;

    [BsonElement("tipo_equipo")]
    public string TipoEquipo { get; set; } = string.Empty;

    [BsonElement("ubicacion")]
    public string Ubicacion { get; set; } = string.Empty;

    [BsonElement("capturado_en")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CapturadoEn { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class DocumentoRepuesto
{
    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("cantidad")]
    public int Cantidad { get; set; }

    [BsonElement("costo_unitario")]
    [BsonIgnoreIfNull]
    public double? CostoUnitario { get; set; }
}

/// <summary>
/// Hojas de servicio sobre MongoDB (HU-005, RT-002). El tenant lo pone y lo filtra la base
/// <see cref="RepositorioMongoTenant{TDocumento}"/> (RN-008). Una intervención firmada no se reemplaza
/// (RN-004): el filtro del reemplazo exige <c>cerrada_firmada</c> en falso. Si MongoDB no responde se
/// lanza <see cref="ErrorServicioNoDisponible"/> antes de tocar PostgreSQL.
/// </summary>
public sealed class IntervencionesMongo(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario)
    : RepositorioMongoTenant<DocumentoIntervencion>(baseDatos, contextoUsuario, Coleccion), IRepositorioIntervenciones
{
    public const string Coleccion = "intervenciones";

    public async Task<IntervencionGuardada?> ObtenerAsync(Guid ordenId, CancellationToken cancellationToken)
    {
        try
        {
            var documento = await ObtenerUnoAsync(Filtro.Eq(d => d.OrdenId, ordenId.ToString()), cancellationToken);
            return documento is null ? null : new IntervencionGuardada(documento.Id.ToString(), Intervencion(documento));
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }
    }

    public async Task<string> GuardarAsync(Intervencion intervencion, string? objectIdExistente, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intervencion);
        var documento = Documento(intervencion);
        try
        {
            if (objectIdExistente is null)
            {
                documento.Id = ObjectId.GenerateNewId();
                await InsertarAsync(documento, cancellationToken);
                return documento.Id.ToString();
            }

            documento.Id = ObjectId.Parse(objectIdExistente);
            var coincidencias = await ReemplazarAsync(
                Filtro.And(Filtro.Eq(d => d.Id, documento.Id), Filtro.Ne(d => d.CerradaFirmada, true)),
                documento,
                cancellationToken);
            return coincidencias > 0
                ? objectIdExistente
                : throw new ErrorConflicto(GestionEjecucion.CodigoFirmada, "La intervención ya está firmada y aprobada; no se puede modificar (RN-004).");
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }
    }

    public async Task FirmarAsync(string objectId, string estadoSalida, FotoEquipo foto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(foto);
        var id = ObjectId.Parse(objectId);
        var snapshot = new DocumentoFotoEquipo
        {
            Nombre = foto.Nombre,
            Marca = foto.Marca,
            Modelo = foto.Modelo,
            TipoEquipo = foto.TipoEquipo,
            Ubicacion = foto.Ubicacion,
            CapturadoEn = foto.CapturadoEn.UtcDateTime,
        };
        try
        {
            // Un documento ya firmado no se toca: así reintentar la aprobación no cambia la copia ni la salida (RN-004).
            await ActualizarAsync(
                Filtro.And(Filtro.Eq(d => d.Id, id), Filtro.Ne(d => d.CerradaFirmada, true)),
                Builders<DocumentoIntervencion>.Update
                    .Set(d => d.CerradaFirmada, true)
                    .Set(d => d.EstadoEquipoSalida, estadoSalida)
                    .Set(d => d.EquipoSnapshot, snapshot),
                cancellationToken);
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }
    }

    private static DocumentoIntervencion Documento(Intervencion i) => new()
    {
        IdIntervencion = i.Id.ToString(),
        EquipoId = i.EquipoId.ToString(),
        OrdenId = i.OrdenId.ToString(),
        TipoMantenimiento = i.TipoMantenimiento,
        FechaIntervencion = i.FechaIntervencion.UtcDateTime,
        TecnicoId = i.TecnicoId.ToString(),
        Diagnostico = i.Diagnostico,
        AccionesRealizadas = i.AccionesRealizadas,
        Repuestos = i.Repuestos
            .Select(r => new DocumentoRepuesto { Nombre = r.Nombre, Cantidad = r.Cantidad, CostoUnitario = (double?)r.CostoUnitario })
            .ToList(),
        CostoManoObra = (double?)i.CostoManoObra,
        CostoTotal = (double)i.CostoTotal,
        TiempoAtencionMin = i.TiempoAtencionMin,
        EstadoEquipoSalida = i.EstadoEquipoSalida,
        CerradaFirmada = i.CerradaFirmada,
        VersionCampos = i.VersionCampos,
        CamposDinamicos = new BsonDocument(i.CamposDinamicos.Select(c => new BsonElement(c.Key, Bson(c.Value)))),
    };

    private static BsonValue Bson(object valor) => valor switch
    {
        long entero => new BsonInt64(entero),
        double numero => new BsonDouble(numero),
        bool logico => BsonBoolean.Create(logico),
        DateOnly fecha => new BsonDateTime(fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        string texto => new BsonString(texto),
        _ => throw new ArgumentException($"Tipo de valor dinámico no admitido: {valor.GetType().Name}.", nameof(valor)),
    };

    private static object Valor(BsonValue valor) => valor.BsonType switch
    {
        BsonType.Int32 or BsonType.Int64 => valor.ToInt64(),
        BsonType.Double => valor.AsDouble,
        BsonType.Boolean => valor.AsBoolean,
        BsonType.DateTime => DateOnly.FromDateTime(valor.ToUniversalTime()),
        _ => valor.IsString ? valor.AsString : valor.ToString() ?? string.Empty,
    };

    private static Intervencion Intervencion(DocumentoIntervencion d) => new(
        Guid.Parse(d.IdIntervencion),
        Guid.Parse(d.OrdenId),
        Guid.Parse(d.EquipoId),
        Guid.Parse(d.TecnicoId),
        d.TipoMantenimiento,
        d.Diagnostico,
        d.AccionesRealizadas,
        d.Repuestos.Select(r => new Repuesto(r.Nombre, r.Cantidad, (decimal?)r.CostoUnitario)).ToList(),
        (decimal?)d.CostoManoObra,
        Math.Round((decimal)d.CostoTotal, 2, MidpointRounding.AwayFromZero),
        d.TiempoAtencionMin,
        d.EstadoEquipoSalida,
        d.CamposDinamicos.ToDictionary(e => e.Name, e => Valor(e.Value), StringComparer.Ordinal),
        d.VersionCampos,
        new DateTimeOffset(DateTime.SpecifyKind(d.FechaIntervencion, DateTimeKind.Utc)),
        d.CerradaFirmada);
}
