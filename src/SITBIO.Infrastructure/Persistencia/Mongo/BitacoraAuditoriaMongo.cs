using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;

namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>
/// Documento de <c>bitacora_auditoria</c> con los nombres del script 02. Los campos opcionales
/// vacíos se omiten: el validador rechaza <c>null</c> donde espera texto u objeto.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class DocumentoBitacora : IDocumentoTenant
{
    [BsonElement("id_evento")]
    public string IdEvento { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [BsonElement("accion")]
    public string Accion { get; set; } = string.Empty;

    [BsonElement("entidad")]
    public string Entidad { get; set; } = string.Empty;

    [BsonElement("entidad_id")]
    [BsonIgnoreIfNull]
    public string? EntidadId { get; set; }

    [BsonElement("ip_origen")]
    [BsonIgnoreIfNull]
    public string? IpOrigen { get; set; }

    [BsonElement("fecha_hora")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime FechaHora { get; set; }

    [BsonElement("detalle")]
    [BsonIgnoreIfNull]
    public Dictionary<string, string>? Detalle { get; set; }
}

/// <summary>
/// Bitácora sobre MongoDB (RF-013, ADR-002). El tenant lo pone y lo filtra la base
/// <see cref="RepositorioMongoTenant{TDocumento}"/> a partir del token (RN-008); solo se inserta y se consulta.
/// </summary>
public sealed class BitacoraAuditoriaMongo(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario)
    : RepositorioMongoTenant<DocumentoBitacora>(baseDatos, contextoUsuario, Coleccion), IBitacoraAuditoria
{
    public const string Coleccion = "bitacora_auditoria";

    public Task RegistrarAsync(EventoAuditoria evento, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evento);
        return InsertarAsync(
            new DocumentoBitacora
            {
                IdEvento = evento.Id.ToString(),
                UsuarioId = evento.UsuarioId.ToString(),
                Accion = evento.Accion,
                Entidad = evento.Entidad,
                EntidadId = evento.EntidadId,
                IpOrigen = evento.IpOrigen,
                FechaHora = evento.FechaHora.UtcDateTime,
                Detalle = evento.Detalle is null ? null : new Dictionary<string, string>(evento.Detalle),
            },
            cancellationToken);
    }

    public async Task<(IReadOnlyList<EventoAuditoria> Items, long Total)> ConsultarAsync(FiltroBitacora filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var condiciones = new List<FilterDefinition<DocumentoBitacora>>();
        if (filtro.UsuarioId is { } usuario)
        {
            condiciones.Add(Filtro.Eq(d => d.UsuarioId, usuario.ToString()));
        }

        if (filtro.Desde is { } desde)
        {
            condiciones.Add(Filtro.Gte(d => d.FechaHora, desde.UtcDateTime));
        }

        if (filtro.Hasta is { } hasta)
        {
            condiciones.Add(Filtro.Lt(d => d.FechaHora, hasta.UtcDateTime));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Accion))
        {
            condiciones.Add(Filtro.Eq(d => d.Accion, filtro.Accion));
        }

        var condicion = condiciones.Count == 0 ? null : Filtro.And(condiciones);
        var total = await ContarAsync(condicion, cancellationToken);

        // Más recientes primero: aprovecha ix_bitacora_tenant_fecha (tenant_id, fecha_hora desc).
        var documentos = await BuscarAsync(
            condicion,
            new FindOptions<DocumentoBitacora, DocumentoBitacora>
            {
                Sort = Builders<DocumentoBitacora>.Sort.Descending(d => d.FechaHora),
                Skip = (Math.Max(filtro.Pagina, 1) - 1) * filtro.Tamano,
                Limit = filtro.Tamano,
            },
            cancellationToken);

        return (documentos.Select(Evento).ToList(), total);
    }

    private static EventoAuditoria Evento(DocumentoBitacora documento) => new(
        Guid.Parse(documento.IdEvento),
        Guid.Parse(documento.UsuarioId),
        documento.Accion,
        documento.Entidad,
        documento.EntidadId,
        new DateTimeOffset(DateTime.SpecifyKind(documento.FechaHora, DateTimeKind.Utc)),
        documento.IpOrigen,
        documento.Detalle);
}
