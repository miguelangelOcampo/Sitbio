using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Configuracion;
using Sitbio.Infrastructure.Persistencia.Mongo;
using Sitbio.Infrastructure.Tests.Aislamiento;

namespace Sitbio.Infrastructure.Tests.Persistencia.Mongo;

/// <summary>
/// Motor de campos dinámicos contra MongoDB real (HU-006). Usa una base temporal cuya colección
/// copia el validador y el índice único de <c>sitbio_dev.configuracion_campos</c> (script 02), así
/// cada documento pasa por el mismo <c>$jsonSchema</c> que en producción. La base se borra al terminar.
/// </summary>
public class ConfiguracionCamposMongoTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariableMongo;

    private readonly string _nombreBase = $"sitbio_pruebas_campos_{Guid.NewGuid():N}";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _tipo = Guid.NewGuid();
    private readonly DateTimeOffset _fecha = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private MongoClient? _cliente;
    private IMongoDatabase? _baseDatos;

    private static readonly CampoDinamico Goteo = new("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h");
    private static readonly CampoDinamico Resultado = new("resultado", "Resultado", TiposDatoCampo.Seleccion, false, ["Pasa", "No pasa"]);

    public async Task InitializeAsync()
    {
        var cadena = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(cadena))
        {
            return;
        }

        _cliente = new MongoClient(cadena);
        _baseDatos = _cliente.GetDatabase(_nombreBase);

        var origen = await (await _cliente.GetDatabase("sitbio_dev").ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", ConfiguracionCamposMongo.Coleccion) })).SingleAsync();
        await _baseDatos.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "create", ConfiguracionCamposMongo.Coleccion },
            { "validator", origen["options"]["validator"].AsBsonDocument },
            { "validationLevel", "strict" },
            { "validationAction", "error" },
        });
        await _baseDatos.GetCollection<BsonDocument>(ConfiguracionCamposMongo.Coleccion).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                new BsonDocument { { "tenant_id", 1 }, { "tipo_equipo", 1 }, { "version", -1 } },
                new CreateIndexOptions { Unique = true, Name = "uq_config_tenant_tipo_version" }));
    }

    public async Task DisposeAsync()
    {
        if (_cliente is not null)
        {
            await _cliente.DropDatabaseAsync(_nombreBase);
        }
    }

    private ConfiguracionCamposMongo Repositorio(Guid tenant)
    {
        var contexto = Substitute.For<IContextoUsuario>();
        contexto.TenantId.Returns(tenant);
        return new ConfiguracionCamposMongo(_baseDatos!, contexto);
    }

    private VersionConfiguracion Version(int numero, params CampoDinamico[] campos) =>
        new(_tipo, numero, EstadosConfiguracion.Activa, Guid.NewGuid(), _fecha.AddDays(numero), campos);

    [FactIntegracion(Variable)]
    public async Task Publicar_GuardaLaVersionYLasAnterioresQuedanHistoricas()
    {
        var repositorio = Repositorio(_tenantA);

        await repositorio.PublicarAsync(Version(1, Goteo), CancellationToken.None);
        await repositorio.PublicarAsync(Version(2, Goteo, Resultado), CancellationToken.None);

        var versiones = await repositorio.VersionesAsync(_tipo, CancellationToken.None);
        Assert.Equal([(2, EstadosConfiguracion.Activa), (1, EstadosConfiguracion.Historica)], versiones.Select(v => (v.Version, v.Estado)));
        Assert.Equal([Goteo, Resultado], versiones[0].Campos);
        Assert.Equal(_fecha.AddDays(2), versiones[0].FechaCreacion);
    }

    [FactIntegracion(Variable)]
    public async Task CA_HU_006_02_OtraInstitucionNoVeLosCampos()
    {
        await Repositorio(_tenantA).PublicarAsync(Version(1, Goteo), CancellationToken.None);

        Assert.Empty(await Repositorio(_tenantB).VersionesAsync(_tipo, CancellationToken.None));

        // La otra institución numera sus versiones aparte y no toca las de la primera.
        await Repositorio(_tenantB).PublicarAsync(Version(1, Resultado), CancellationToken.None);
        var propias = await Repositorio(_tenantA).VersionesAsync(_tipo, CancellationToken.None);
        Assert.Equal(EstadosConfiguracion.Activa, Assert.Single(propias).Estado);
        Assert.Equal([Goteo], propias[0].Campos);
    }

    [FactIntegracion(Variable)]
    public async Task Publicar_MismaVersionDosVeces_Responde409()
    {
        var repositorio = Repositorio(_tenantA);
        await repositorio.PublicarAsync(Version(1, Goteo), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => repositorio.PublicarAsync(Version(1, Resultado), CancellationToken.None));

        Assert.Equal(ErroresConfiguracionCampos.CodigoVersionConcurrente, error.Codigo);
        Assert.Single(await repositorio.VersionesAsync(_tipo, CancellationToken.None));
    }
}
