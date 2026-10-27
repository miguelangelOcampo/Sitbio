using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Persistencia.Mongo;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// RN-008 / DRV-001 en MongoDB: dos tenants en la misma colección; ninguno ve ni
/// modifica los documentos del otro. Usa una colección temporal que se borra al final.
/// </summary>
public class AislamientoTenantMongoTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariableMongo;
    private const string BaseDatos = "sitbio_pruebas";

    private readonly string _coleccion = $"prueba_aislamiento_tenant_{Guid.NewGuid():N}";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private IMongoDatabase? _baseDatos;

    public sealed class DocumentoDePrueba : IDocumentoTenant
    {
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [BsonElement("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [BsonElement("estado")]
        public string Estado { get; set; } = "abierto";
    }

    private sealed class RepositorioDePrueba(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario, string coleccion)
        : RepositorioMongoTenant<DocumentoDePrueba>(baseDatos, contextoUsuario, coleccion)
    {
        public Task<List<DocumentoDePrueba>> Todos() => BuscarAsync();

        public Task<long> Contar() => ContarAsync();

        public Task Insertar(DocumentoDePrueba documento) => InsertarAsync(documento);

        public Task<long> Cerrar(string id) =>
            ActualizarAsync(Filtro.Eq(d => d.Id, id), Builders<DocumentoDePrueba>.Update.Set(d => d.Estado, "cerrado"));
    }

    private RepositorioDePrueba CrearRepositorio(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        return new RepositorioDePrueba(_baseDatos!, contextoUsuario, _coleccion);
    }

    public Task InitializeAsync()
    {
        var cadena = Environment.GetEnvironmentVariable(Variable);
        _baseDatos = new MongoClient(cadena).GetDatabase(BaseDatos);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _baseDatos!.DropCollectionAsync(_coleccion);

    [FactIntegracion(Variable)]
    public async Task Buscar_CadaTenantSoloVeSusDocumentos()
    {
        var repoA = CrearRepositorio(_tenantA);
        var repoB = CrearRepositorio(_tenantB);
        await repoA.Insertar(new DocumentoDePrueba());
        await repoA.Insertar(new DocumentoDePrueba());
        await repoB.Insertar(new DocumentoDePrueba());

        var deA = await repoA.Todos();

        Assert.Equal(2, deA.Count);
        Assert.All(deA, d => Assert.Equal(_tenantA.ToString(), d.TenantId));
        Assert.Equal(2, await repoA.Contar());
        Assert.Equal(1, await repoB.Contar());
    }

    [FactIntegracion(Variable)]
    public async Task Actualizar_DocumentoDeOtroTenant_NoModificaNada()
    {
        var repoA = CrearRepositorio(_tenantA);
        var repoB = CrearRepositorio(_tenantB);
        var deB = new DocumentoDePrueba();
        await repoB.Insertar(deB);

        var modificados = await repoA.Cerrar(deB.Id);

        Assert.Equal(0, modificados);
        Assert.Equal("abierto", (await repoB.Todos()).Single().Estado);
    }

    [FactIntegracion(Variable)]
    public async Task Insertar_ConTenantAjeno_QuedaConElTenantDelToken()
    {
        var repoA = CrearRepositorio(_tenantA);
        var repoB = CrearRepositorio(_tenantB);

        await repoA.Insertar(new DocumentoDePrueba { TenantId = _tenantB.ToString() });

        Assert.Equal(1, await repoA.Contar());
        Assert.Equal(0, await repoB.Contar());
    }
}
