using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Persistencia.Mongo;
using Sitbio.Infrastructure.Tests.Aislamiento;

namespace Sitbio.Infrastructure.Tests.Auditoria;

/// <summary>
/// Bitácora contra MongoDB real (RF-013, HU-010). Usa una base temporal cuya colección copia el
/// validador de <c>sitbio_dev.bitacora_auditoria</c> (script 02 + V007), así cada documento escrito
/// pasa por el mismo <c>$jsonSchema</c> que en producción. La base se borra al terminar.
/// </summary>
public class BitacoraAuditoriaMongoTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariableMongo;

    private readonly string _nombreBase = $"sitbio_pruebas_bitacora_{Guid.NewGuid():N}";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly DateTimeOffset _base = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);
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

        // Copia el validador vigente de la base de desarrollo (02 + V007).
        var origen = await (await _cliente.GetDatabase("sitbio_dev").ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", BitacoraAuditoriaMongo.Coleccion) })).SingleAsync();
        var validador = origen["options"]["validator"].AsBsonDocument;
        Assert.Contains("ACCESO_DENEGADO", validador.ToJson());
        await _baseDatos.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "create", BitacoraAuditoriaMongo.Coleccion },
            { "validator", validador },
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

    private BitacoraAuditoriaMongo Bitacora(Guid tenant)
    {
        var contexto = Substitute.For<IContextoUsuario>();
        contexto.TenantId.Returns(tenant);
        return new BitacoraAuditoriaMongo(_baseDatos!, contexto);
    }

    private EventoAuditoria Evento(Guid usuario, string accion, int minutos, string? entidadId = null) =>
        EventoAuditoria.Nuevo(usuario, accion, "usuarios", _base.AddMinutes(minutos), entidadId);

    [FactIntegracion(Variable)]
    public async Task Registrar_GuardaTodosLosCamposYLosDevuelveIguales()
    {
        var usuario = Guid.NewGuid();
        var evento = EventoAuditoria.Nuevo(usuario, AccionesAuditoria.AccesoDenegado, "usuarios", _base, Guid.NewGuid().ToString(), "10.0.0.5",
            new Dictionary<string, string> { ["metodo"] = "POST", ["ruta"] = "/api/v1/usuarios" });

        await Bitacora(_tenantA).RegistrarAsync(evento, CancellationToken.None);

        var (items, total) = await Bitacora(_tenantA).ConsultarAsync(new FiltroBitacora(), CancellationToken.None);
        Assert.Equal(1, total);
        var leido = Assert.Single(items);
        Assert.Equal(evento.Id, leido.Id);
        Assert.Equal(evento.UsuarioId, leido.UsuarioId);
        Assert.Equal(evento.Accion, leido.Accion);
        Assert.Equal(evento.EntidadId, leido.EntidadId);
        Assert.Equal(evento.IpOrigen, leido.IpOrigen);
        Assert.Equal(evento.FechaHora, leido.FechaHora);
        Assert.Equal("/api/v1/usuarios", leido.Detalle?["ruta"]);

        var crudo = await _baseDatos!.GetCollection<BsonDocument>(BitacoraAuditoriaMongo.Coleccion).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(_tenantA.ToString(), crudo["tenant_id"].AsString);
        Assert.Equal(BsonType.DateTime, crudo["fecha_hora"].BsonType);
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_SinCamposOpcionales_PasaElValidador()
    {
        await Bitacora(_tenantA).RegistrarAsync(EventoAuditoria.Nuevo(Guid.NewGuid(), AccionesAuditoria.CierreSesion, "sesion", _base), CancellationToken.None);

        var crudo = await _baseDatos!.GetCollection<BsonDocument>(BitacoraAuditoriaMongo.Coleccion).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.False(crudo.Contains("entidad_id"));
        Assert.False(crudo.Contains("ip_origen"));
        Assert.False(crudo.Contains("detalle"));
    }

    [FactIntegracion(Variable)]
    public async Task ElValidadorRechazaUnaAccionFueraDelCatalogo()
    {
        var crudo = new BsonDocument
        {
            { "id_evento", Guid.NewGuid().ToString() }, { "tenant_id", _tenantA.ToString() }, { "usuario_id", Guid.NewGuid().ToString() },
            { "accion", "INVENTADA" }, { "entidad", "usuarios" }, { "fecha_hora", DateTime.UtcNow },
        };

        await Assert.ThrowsAsync<MongoWriteException>(() =>
            _baseDatos!.GetCollection<BsonDocument>(BitacoraAuditoriaMongo.Coleccion).InsertOneAsync(crudo));
    }

    [FactIntegracion(Variable)]
    public async Task CA_HU_010_03_Consultar_FiltraPorUsuarioFechaYAccionDelMasRecienteAlMasAntiguo()
    {
        var bitacora = Bitacora(_tenantA);
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();
        await bitacora.RegistrarAsync(Evento(ana, AccionesAuditoria.LoginOk, 0), CancellationToken.None);
        await bitacora.RegistrarAsync(Evento(ana, AccionesAuditoria.AccesoDenegado, 10), CancellationToken.None);
        await bitacora.RegistrarAsync(Evento(ana, AccionesAuditoria.AccesoDenegado, 20), CancellationToken.None);
        await bitacora.RegistrarAsync(Evento(luis, AccionesAuditoria.AccesoDenegado, 15), CancellationToken.None);

        var (deAna, totalAna) = await bitacora.ConsultarAsync(new FiltroBitacora(UsuarioId: ana), CancellationToken.None);
        Assert.Equal(3, totalAna);
        Assert.Equal([_base.AddMinutes(20), _base.AddMinutes(10), _base], deAna.Select(e => e.FechaHora));

        var (rango, _) = await bitacora.ConsultarAsync(new FiltroBitacora(Desde: _base.AddMinutes(10), Hasta: _base.AddMinutes(20)), CancellationToken.None);
        Assert.Equal([_base.AddMinutes(15), _base.AddMinutes(10)], rango.Select(e => e.FechaHora));

        var (denegados, totalDenegados) = await bitacora.ConsultarAsync(new FiltroBitacora(UsuarioId: ana, Accion: AccionesAuditoria.AccesoDenegado), CancellationToken.None);
        Assert.Equal(2, totalDenegados);
        Assert.All(denegados, e => Assert.Equal(AccionesAuditoria.AccesoDenegado, e.Accion));
    }

    [FactIntegracion(Variable)]
    public async Task Consultar_PaginaConElTotalCompleto()
    {
        var bitacora = Bitacora(_tenantA);
        var usuario = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await bitacora.RegistrarAsync(Evento(usuario, AccionesAuditoria.LoginOk, i), CancellationToken.None);
        }

        var (pagina2, total) = await bitacora.ConsultarAsync(new FiltroBitacora(Pagina: 2, Tamano: 2), CancellationToken.None);

        Assert.Equal(5, total);
        Assert.Equal([_base.AddMinutes(2), _base.AddMinutes(1)], pagina2.Select(e => e.FechaHora));
    }

    [FactIntegracion(Variable)]
    public async Task CA_HU_010_02_CadaTenantSoloVeSuBitacora()
    {
        var usuario = Guid.NewGuid();
        await Bitacora(_tenantA).RegistrarAsync(Evento(usuario, AccionesAuditoria.LoginOk, 0), CancellationToken.None);
        await Bitacora(_tenantA).RegistrarAsync(Evento(usuario, AccionesAuditoria.AccesoDenegado, 1), CancellationToken.None);
        await Bitacora(_tenantB).RegistrarAsync(Evento(Guid.NewGuid(), AccionesAuditoria.LoginOk, 2), CancellationToken.None);

        var (deB, totalB) = await Bitacora(_tenantB).ConsultarAsync(new FiltroBitacora(), CancellationToken.None);
        var (porUsuarioDeA, totalCruzado) = await Bitacora(_tenantB).ConsultarAsync(new FiltroBitacora(UsuarioId: usuario), CancellationToken.None);

        Assert.Equal(1, totalB);
        Assert.Single(deB);
        Assert.Equal(0, totalCruzado);
        Assert.Empty(porUsuarioDeA);
    }
}
