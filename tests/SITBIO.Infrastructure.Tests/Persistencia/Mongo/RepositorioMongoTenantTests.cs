using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Persistencia.Mongo;

namespace Sitbio.Infrastructure.Tests.Persistencia.Mongo;

public class RepositorioMongoTenantTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly IMongoCollection<DocumentoDePrueba> _coleccion = Substitute.For<IMongoCollection<DocumentoDePrueba>>();
    private readonly RepositorioDePrueba _repositorio;

    public RepositorioMongoTenantTests()
    {
        var baseDatos = Substitute.For<IMongoDatabase>();
        baseDatos.GetCollection<DocumentoDePrueba>("pruebas", Arg.Any<MongoCollectionSettings>()).Returns(_coleccion);
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(_tenant);
        _repositorio = new RepositorioDePrueba(baseDatos, contextoUsuario);
    }

    public sealed class DocumentoDePrueba : IDocumentoTenant
    {
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [BsonElement("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [BsonElement("estado")]
        public string Estado { get; set; } = string.Empty;
    }

    private sealed class RepositorioDePrueba(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario)
        : RepositorioMongoTenant<DocumentoDePrueba>(baseDatos, contextoUsuario, "pruebas")
    {
        public Task<List<DocumentoDePrueba>> Buscar(string estado) => BuscarAsync(Filtro.Eq(d => d.Estado, estado));

        public Task<long> Contar() => ContarAsync();

        public Task Insertar(DocumentoDePrueba documento) => InsertarAsync(documento);

        public Task<long> Actualizar(string id) =>
            ActualizarAsync(Filtro.Eq(d => d.Id, id), Builders<DocumentoDePrueba>.Update.Set(d => d.Estado, "cerrado"));
    }

    private static BsonDocument Renderizar(FilterDefinition<DocumentoDePrueba> filtro) =>
        filtro.Render(new RenderArgs<DocumentoDePrueba>(
            BsonSerializer.LookupSerializer<DocumentoDePrueba>(), BsonSerializer.SerializerRegistry));

    private void AssertFiltraPorTenant(FilterDefinition<DocumentoDePrueba> filtro)
    {
        var json = Renderizar(filtro).ToJson();
        Assert.Contains($"\"tenant_id\" : \"{_tenant}\"", json);
    }

    [Fact]
    public async Task Buscar_AgregaElTenantDelToken()
    {
        FilterDefinition<DocumentoDePrueba>? enviado = null;
        var cursor = Substitute.For<IAsyncCursor<DocumentoDePrueba>>();
        _coleccion.FindAsync(
                Arg.Do<FilterDefinition<DocumentoDePrueba>>(f => enviado = f),
                Arg.Any<FindOptions<DocumentoDePrueba, DocumentoDePrueba>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

        await _repositorio.Buscar("abierto");

        AssertFiltraPorTenant(enviado!);
        Assert.Contains("\"estado\" : \"abierto\"", Renderizar(enviado!).ToJson());
    }

    [Fact]
    public async Task Contar_AgregaElTenantDelToken()
    {
        FilterDefinition<DocumentoDePrueba>? enviado = null;
        _coleccion.CountDocumentsAsync(
                Arg.Do<FilterDefinition<DocumentoDePrueba>>(f => enviado = f),
                Arg.Any<CountOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(0L);

        await _repositorio.Contar();

        AssertFiltraPorTenant(enviado!);
    }

    [Fact]
    public async Task Actualizar_AgregaElTenantDelToken()
    {
        FilterDefinition<DocumentoDePrueba>? enviado = null;
        _coleccion.UpdateManyAsync(
                Arg.Do<FilterDefinition<DocumentoDePrueba>>(f => enviado = f),
                Arg.Any<UpdateDefinition<DocumentoDePrueba>>(),
                Arg.Any<UpdateOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateResult.Acknowledged(1, 1, null));

        await _repositorio.Actualizar("doc-1");

        AssertFiltraPorTenant(enviado!);
    }

    [Fact]
    public async Task Insertar_ReemplazaElTenantAjenoPorElDelToken()
    {
        var documento = new DocumentoDePrueba { TenantId = Guid.NewGuid().ToString() };

        await _repositorio.Insertar(documento);

        Assert.Equal(_tenant.ToString(), documento.TenantId);
        await _coleccion.Received(1).InsertOneAsync(documento, Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>());
    }
}
