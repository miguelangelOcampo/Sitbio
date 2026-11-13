using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Infrastructure.Persistencia.Mongo;
using Sitbio.Infrastructure.Tests.Aislamiento;

namespace Sitbio.Infrastructure.Tests.Persistencia.Mongo;

/// <summary>
/// Intervenciones contra MongoDB real (HU-005). La colección temporal copia el validador de
/// <c>sitbio_dev.intervenciones</c> (script 02): cada documento pasa por el mismo <c>$jsonSchema</c>
/// que en producción. La base se borra al terminar.
/// </summary>
public class IntervencionesMongoTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariableMongo;

    private readonly string _nombreBase = $"sitbio_pruebas_intervenciones_{Guid.NewGuid():N}";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly DateTimeOffset _fecha = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private MongoClient? _cliente;
    private IMongoDatabase? _baseDatos;

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
            new ListCollectionsOptions { Filter = new BsonDocument("name", IntervencionesMongo.Coleccion) })).SingleAsync();
        await _baseDatos.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "create", IntervencionesMongo.Coleccion },
            { "validator", origen["options"]["validator"].AsBsonDocument },
            { "validationLevel", "strict" },
            { "validationAction", "error" },
        });
    }

    public async Task DisposeAsync()
    {
        if (_cliente is not null)
        {
            await _cliente.DropDatabaseAsync(_nombreBase);
        }
    }

    private IntervencionesMongo Repositorio(Guid tenant, IMongoDatabase? baseDatos = null)
    {
        var contexto = Substitute.For<IContextoUsuario>();
        contexto.TenantId.Returns(tenant);
        return new IntervencionesMongo(baseDatos ?? _baseDatos!, contexto);
    }

    private Intervencion Nueva(Guid ordenId, string diagnostico = "Sensor descalibrado") => new(
        Guid.NewGuid(),
        ordenId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        TiposMantenimiento.Correctivo,
        diagnostico,
        "Calibración del sensor",
        [new Repuesto("Sensor de presión", 2, 15000.5m), new Repuesto("Tornillo", 4, null)],
        40000m,
        70001m,
        90,
        EstadosSalidaEquipo.Operativo,
        new Dictionary<string, object>
        {
            ["tasa_goteo"] = 25.5,
            ["ciclos"] = 3L,
            ["resultado"] = "Pasa",
            ["calibrado"] = true,
            ["vence"] = new DateOnly(2027, 5, 1),
        },
        2,
        _fecha,
        false);

    [FactIntegracion(Variable)]
    public async Task Guardar_PasaElValidador_YSeLeeIgualConSuObjectId()
    {
        var repositorio = Repositorio(_tenantA);
        var intervencion = Nueva(Guid.NewGuid());

        var objectId = await repositorio.GuardarAsync(intervencion, null, CancellationToken.None);

        Assert.Matches("^[0-9a-f]{24}$", objectId);
        var leida = (await repositorio.ObtenerAsync(intervencion.OrdenId, CancellationToken.None))!;
        Assert.Equal(objectId, leida.ObjectId);
        var i = leida.Intervencion;
        Assert.Equal((intervencion.Id, intervencion.OrdenId, intervencion.EquipoId, intervencion.TecnicoId), (i.Id, i.OrdenId, i.EquipoId, i.TecnicoId));
        Assert.Equal(intervencion.Repuestos, i.Repuestos);
        Assert.Equal(((decimal?)40000m, 70001m, (int?)90, (int?)2, _fecha), (i.CostoManoObra, i.CostoTotal, i.TiempoAtencionMin, i.VersionCampos, i.FechaIntervencion));
        Assert.Equal(intervencion.CamposDinamicos.OrderBy(c => c.Key), i.CamposDinamicos.OrderBy(c => c.Key));

        // Lo que quedó en la colección usa los nombres y tipos del script 02.
        var documento = await _baseDatos!.GetCollection<BsonDocument>(IntervencionesMongo.Coleccion).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(_tenantA.ToString(), documento["tenant_id"].AsString);
        Assert.Equal(BsonType.DateTime, documento["fecha_intervencion"].BsonType);
        Assert.Equal(BsonType.Int64, documento["camposDinamicos"]["ciclos"].BsonType);
    }

    [FactIntegracion(Variable)]
    public async Task Reemplazar_CorrigeElMismoDocumento_YOtraInstitucionNoLoVe()
    {
        var repositorio = Repositorio(_tenantA);
        var intervencion = Nueva(Guid.NewGuid());
        var objectId = await repositorio.GuardarAsync(intervencion, null, CancellationToken.None);

        var otro = Repositorio(_tenantB);
        Assert.Null(await otro.ObtenerAsync(intervencion.OrdenId, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorConflicto>(() => otro.GuardarAsync(intervencion, objectId, CancellationToken.None));

        Assert.Equal(objectId, await repositorio.GuardarAsync(intervencion with { Diagnostico = "Corregido" }, objectId, CancellationToken.None));
        Assert.Equal("Corregido", (await repositorio.ObtenerAsync(intervencion.OrdenId, CancellationToken.None))!.Intervencion.Diagnostico);
        Assert.Equal(1, await _baseDatos!.GetCollection<BsonDocument>(IntervencionesMongo.Coleccion).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [FactIntegracion(Variable)]
    public async Task Reemplazar_UnaIntervencionFirmada_SeRechazaRn004()
    {
        var repositorio = Repositorio(_tenantA);
        var intervencion = Nueva(Guid.NewGuid());
        var objectId = await repositorio.GuardarAsync(intervencion with { CerradaFirmada = true }, null, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            repositorio.GuardarAsync(intervencion with { Diagnostico = "Cambio" }, objectId, CancellationToken.None));

        Assert.Equal(GestionEjecucion.CodigoFirmada, error.Codigo);
        Assert.Equal("Sensor descalibrado", (await repositorio.ObtenerAsync(intervencion.OrdenId, CancellationToken.None))!.Intervencion.Diagnostico);
    }

    [FactIntegracion(Variable)]
    public async Task Firmar_MarcaCerradaFirmadaConSalidaYCopiaDelEquipo_YYaNoSeModifica()
    {
        var repositorio = Repositorio(_tenantA);
        var intervencion = Nueva(Guid.NewGuid()) with { EstadoEquipoSalida = EstadosSalidaEquipo.EnMantenimiento };
        var objectId = await repositorio.GuardarAsync(intervencion, null, CancellationToken.None);
        var foto = new FotoEquipo("Bomba de infusión PL-001", "Baxter", "Sigma", "Bomba de infusión", "UCI", _fecha.AddHours(2));

        // Otra institución no la firma (RN-008).
        await Repositorio(_tenantB).FirmarAsync(objectId, EstadosSalidaEquipo.Operativo, foto, CancellationToken.None);
        Assert.False((await repositorio.ObtenerAsync(intervencion.OrdenId, CancellationToken.None))!.Intervencion.CerradaFirmada);

        await repositorio.FirmarAsync(objectId, EstadosSalidaEquipo.Operativo, foto, CancellationToken.None);
        // Repetir la firma (reintento de la aprobación) no cambia la copia ni la salida.
        await repositorio.FirmarAsync(objectId, EstadosSalidaEquipo.Baja, foto with { Ubicacion = "Otra" }, CancellationToken.None);

        var leida = (await repositorio.ObtenerAsync(intervencion.OrdenId, CancellationToken.None))!.Intervencion;
        Assert.Equal((true, EstadosSalidaEquipo.Operativo), (leida.CerradaFirmada, leida.EstadoEquipoSalida));
        var documento = await _baseDatos!.GetCollection<BsonDocument>(IntervencionesMongo.Coleccion).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        var copia = documento["equipo_snapshot"].AsBsonDocument;
        Assert.Equal(("Bomba de infusión PL-001", "Baxter", "Sigma", "Bomba de infusión", "UCI"),
            (copia["nombre"].AsString, copia["marca"].AsString, copia["modelo"].AsString, copia["tipo_equipo"].AsString, copia["ubicacion"].AsString));
        Assert.Equal(BsonType.DateTime, copia["capturado_en"].BsonType);

        // RN-004: firmada, el reemplazo se rechaza.
        await Assert.ThrowsAsync<ErrorConflicto>(() => repositorio.GuardarAsync(intervencion with { Diagnostico = "Cambio" }, objectId, CancellationToken.None));
    }

    [Fact]
    public async Task MongoSinRespuesta_Responde503()
    {
        var cliente = new MongoClient(new MongoClientSettings
        {
            Server = new MongoServerAddress("127.0.0.1", 1),
            ServerSelectionTimeout = TimeSpan.FromMilliseconds(200),
            ConnectTimeout = TimeSpan.FromMilliseconds(200),
        });
        var repositorio = Repositorio(_tenantA, cliente.GetDatabase("sitbio_inaccesible"));

        await Assert.ThrowsAsync<ErrorServicioNoDisponible>(() => repositorio.GuardarAsync(Nueva(Guid.NewGuid()), null, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorServicioNoDisponible>(() => repositorio.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorServicioNoDisponible>(() => repositorio.FirmarAsync(
            ObjectId.GenerateNewId().ToString(), EstadosSalidaEquipo.Operativo, new FotoEquipo("N", "M", "Mo", "T", "U", _fecha), CancellationToken.None));
    }
}
