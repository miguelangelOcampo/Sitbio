namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>
/// Documento de MongoDB que pertenece a una institución. La propiedad se guarda
/// en el campo <c>tenant_id</c> (texto UUID), como define 02_MongoDB_SITBIO.js;
/// las implementaciones la marcan con <c>[BsonElement("tenant_id")]</c>.
/// </summary>
public interface IDocumentoTenant
{
    string TenantId { get; set; }
}
