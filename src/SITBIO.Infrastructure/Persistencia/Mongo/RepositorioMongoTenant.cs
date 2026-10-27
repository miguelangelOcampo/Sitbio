using MongoDB.Driver;
using Sitbio.Application.Comun;

namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>
/// Base de los repositorios de MongoDB (CMP-MONGO). Toda lectura, conteo y
/// actualización lleva el filtro <c>tenant_id</c> del token, y toda inserción
/// toma el tenant del token (RN-008, SEC-003). No ofrece eliminación: la bitácora
/// solo se agrega (HU-010) y una intervención firmada no se borra (RN-004).
/// </summary>
public abstract class RepositorioMongoTenant<TDocumento>
    where TDocumento : IDocumentoTenant
{
    public const string CampoTenant = "tenant_id";

    private readonly IMongoCollection<TDocumento> _coleccion;
    private readonly IContextoUsuario _contextoUsuario;

    protected RepositorioMongoTenant(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario, string nombreColeccion)
    {
        _coleccion = baseDatos.GetCollection<TDocumento>(nombreColeccion);
        _contextoUsuario = contextoUsuario;
    }

    protected static FilterDefinitionBuilder<TDocumento> Filtro => Builders<TDocumento>.Filter;

    private string TenantActual => _contextoUsuario.TenantId.ToString();

    /// <summary>Agrega el tenant del token al filtro recibido.</summary>
    protected FilterDefinition<TDocumento> FiltroTenant(FilterDefinition<TDocumento>? filtro = null)
    {
        var porTenant = Filtro.Eq(CampoTenant, TenantActual);
        return filtro is null ? porTenant : Filtro.And(porTenant, filtro);
    }

    protected async Task<List<TDocumento>> BuscarAsync(
        FilterDefinition<TDocumento>? filtro = null,
        FindOptions<TDocumento, TDocumento>? opciones = null,
        CancellationToken cancellationToken = default)
    {
        using var cursor = await _coleccion.FindAsync(FiltroTenant(filtro), opciones, cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    protected async Task<TDocumento?> ObtenerUnoAsync(
        FilterDefinition<TDocumento> filtro,
        CancellationToken cancellationToken = default)
    {
        var encontrados = await BuscarAsync(filtro, new FindOptions<TDocumento, TDocumento> { Limit = 1 }, cancellationToken);
        return encontrados.FirstOrDefault();
    }

    protected Task<long> ContarAsync(
        FilterDefinition<TDocumento>? filtro = null,
        CancellationToken cancellationToken = default)
    {
        return _coleccion.CountDocumentsAsync(FiltroTenant(filtro), null, cancellationToken);
    }

    protected Task InsertarAsync(TDocumento documento, CancellationToken cancellationToken = default)
    {
        documento.TenantId = TenantActual;
        return _coleccion.InsertOneAsync(documento, null, cancellationToken);
    }

    protected async Task<long> ActualizarAsync(
        FilterDefinition<TDocumento> filtro,
        UpdateDefinition<TDocumento> cambios,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _coleccion.UpdateManyAsync(FiltroTenant(filtro), cambios, null, cancellationToken);
        return resultado.ModifiedCount;
    }
}
