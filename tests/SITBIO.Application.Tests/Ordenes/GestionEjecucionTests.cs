using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Ordenes;

/// <summary>
/// Ejecución de la orden por el técnico asignado (HU-005): iniciar y pausar, el tiquete «en ejecución», la
/// intervención validada contra la versión activa de los campos dinámicos y RT-002 (si MongoDB falla, la
/// orden no cambia).
/// </summary>
public class GestionEjecucionTests
{
    private const string ObjectIdNuevo = "65f1a2b3c4d5e6f7a8b9c0d1";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private static readonly CampoDinamico Goteo = new("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h");
    private static readonly CampoDinamico Ciclos = new("ciclos", "Ciclos", TiposDatoCampo.Numero, false);
    private static readonly CampoDinamico Resultado = new("resultado", "Resultado", TiposDatoCampo.Seleccion, true, ["Pasa", "No pasa"]);
    private static readonly CampoDinamico Calibrado = new("calibrado", "Calibrado", TiposDatoCampo.Booleano, false);
    private static readonly CampoDinamico Vence = new("vence", "Vence", TiposDatoCampo.Fecha, false);

    private readonly IRepositorioOrdenes _ordenes = Substitute.For<IRepositorioOrdenes>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioConfiguracionCampos _configuracion = Substitute.For<IRepositorioConfiguracionCampos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Guid _tipo = Guid.NewGuid();
    private readonly Tiquete _tiquete;
    private readonly OrdenTrabajo _orden;
    private readonly GestionEjecucion _caso;
    private Intervencion? _guardada;

    public GestionEjecucionTests()
    {
        _tiquete = new Tiquete(Tenant, Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, Criticidades.Alta, Ahora.AddHours(-2));
        _tiquete.Numerar(9);
        (_orden, _) = OrdenTrabajo.Correctiva(_tiquete, _tecnico, Guid.NewGuid(), null, null, Ahora.AddHours(-1));

        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_tecnico);
        _contexto.Rol.Returns(CodigosRol.TecnicoMantenimiento);
        _ordenes.OrdenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OrdenTrabajo?)null);
        _ordenes.OrdenAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(_orden);
        _ordenes.ObtenerAsync(_orden.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new OrdenLeida(_orden, "PL-001", "SN-1", "Bomba de infusión", "UCI", "Carlos Ruiz", _tiquete.Codigo, _tiquete.TipoFalla));
        _ordenes.TiqueteAsync(_tiquete.Id, Arg.Any<CancellationToken>()).Returns(_tiquete);
        _ordenes.TipoEquipoAsync(_orden.EquipoId, Arg.Any<CancellationToken>()).Returns(_tipo);
        _configuracion.VersionesAsync(_tipo, Arg.Any<CancellationToken>()).Returns(
        [
            new VersionConfiguracion(_tipo, 2, EstadosConfiguracion.Activa, Guid.NewGuid(), Ahora.AddDays(-1), [Goteo, Ciclos, Resultado, Calibrado, Vence]),
            new VersionConfiguracion(_tipo, 1, EstadosConfiguracion.Historica, Guid.NewGuid(), Ahora.AddDays(-9), [Goteo]),
        ]);
        _intervenciones.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IntervencionGuardada?)null);
        _intervenciones.GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(llamada =>
            {
                _guardada = llamada.ArgAt<Intervencion>(0);
                return llamada.ArgAt<string?>(1) ?? ObjectIdNuevo;
            });

        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionEjecucion(_ordenes, _intervenciones, _configuracion, _auditoria, _contexto, tiempo);
    }

    private static IReadOnlyDictionary<string, JsonElement> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static DatosIntervencion Datos(string camposJson = """{"tasa_goteo": 25.5, "resultado": "pasa"}""") => new(
        "Sensor de presión descalibrado",
        "Se calibró el sensor y se probó con el analizador",
        [new DatosRepuesto("Sensor de presión", 2, 15000m), new DatosRepuesto("Tornillo", 4, null)],
        CostoManoObra: 40000m,
        TiempoAtencionMin: 90,
        EstadoEquipoSalida: "operativo",
        CamposDinamicos: Json(camposJson));

    // ---------- Iniciar y pausar ----------

    [Fact]
    public async Task Iniciar_LaPrimeraVez_LaOrdenYElTiquetePasanAEnEjecucion()
    {
        var dto = await _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(" Iniciar "), CancellationToken.None);

        Assert.Equal((EstadosOrden.EnEjecucion, (DateTimeOffset?)Ahora), (dto.Estado, dto.FechaInicio));
        Assert.Equal(EstadosTiquete.EnEjecucion, _tiquete.Estado);
        await _ordenes.Received(1).GuardarAsync(
            Arg.Is<HistorialTiquete?>(h => h != null && h.EstadoAnterior == EstadosTiquete.Asignado && h.NuevoEstado == EstadosTiquete.EnEjecucion && h.UsuarioId == _tecnico),
            Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _tecnico,
            AccionesAuditoria.Actualizar,
            GestionOrdenes.Entidad,
            _orden.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["accion"] == AccionesOrden.Iniciar && d["estado_anterior"] == EstadosOrden.Asignada && d["estado"] == EstadosOrden.EnEjecucion),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PausarYReanudar_NoTocanElTiqueteNiLaFechaDeInicio()
    {
        await _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(AccionesOrden.Iniciar), CancellationToken.None);
        _ordenes.ClearReceivedCalls();

        Assert.Equal(EstadosOrden.Pausada, (await _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(AccionesOrden.Pausar), CancellationToken.None)).Estado);
        var reanudada = await _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(AccionesOrden.Iniciar), CancellationToken.None);

        Assert.Equal((EstadosOrden.EnEjecucion, (DateTimeOffset?)Ahora), (reanudada.Estado, reanudada.FechaInicio));
        await _ordenes.Received(2).GuardarAsync(null, Arg.Any<CancellationToken>());
        await _ordenes.DidNotReceive().GuardarAsync(Arg.Is<HistorialTiquete?>(h => h != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pausar_UnaOrdenQueNoEstaEnEjecucion_Responde409SinGuardar()
    {
        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(AccionesOrden.Pausar), CancellationToken.None));

        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, error.Codigo);
        Assert.Equal(EstadosOrden.Asignada, _orden.Estado);
        await _ordenes.DidNotReceive().GuardarAsync(Arg.Any<HistorialTiquete?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cerrar")]
    public async Task AccionDesconocida_Responde400(string? accion)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(accion), CancellationToken.None));

        Assert.Equal(["accion"], error.Errores.Keys);
    }

    [Fact]
    public async Task OrdenDeOtroTecnico_SeRespondeComoNoEncontrada()
    {
        _contexto.UsuarioId.Returns(Guid.NewGuid());

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.CambiarEstadoAsync(_orden.Id, new DatosEstadoOrden(AccionesOrden.Iniciar), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.CambiarEstadoAsync(Guid.NewGuid(), new DatosEstadoOrden(AccionesOrden.Iniciar), CancellationToken.None));
        Assert.Equal(EstadosOrden.Asignada, _orden.Estado);
    }

    // ---------- Intervención ----------

    [Fact]
    public async Task Registrar_GuardaEnMongoConValoresTipados_Y_EnlazaLaOrden()
    {
        _orden.Iniciar(Ahora);

        var dto = await _caso.RegistrarIntervencionAsync(
            _orden.Id,
            Datos("""{"tasa_goteo": 25.5, "ciclos": 3, "resultado": "pasa", "calibrado": true, "vence": "2027-05-01"}"""),
            CancellationToken.None);

        var i = _guardada!;
        Assert.Equal((_orden.Id, _orden.EquipoId, _tecnico, TiposMantenimiento.Correctivo), (i.OrdenId, i.EquipoId, i.TecnicoId, i.TipoMantenimiento));
        Assert.Equal(("Sensor de presión descalibrado", EstadosSalidaEquipo.Operativo, (int?)90), (i.Diagnostico, i.EstadoEquipoSalida, i.TiempoAtencionMin));
        Assert.Equal([new Repuesto("Sensor de presión", 2, 15000m), new Repuesto("Tornillo", 4, null)], i.Repuestos);
        Assert.Equal(70000m, i.CostoTotal);
        Assert.Equal((int?)2, i.VersionCampos);
        Assert.Equal(25.5, i.CamposDinamicos["tasa_goteo"]);
        Assert.Equal(3L, i.CamposDinamicos["ciclos"]);
        Assert.Equal("Pasa", i.CamposDinamicos["resultado"]);
        Assert.Equal(true, i.CamposDinamicos["calibrado"]);
        Assert.Equal(new DateOnly(2027, 5, 1), i.CamposDinamicos["vence"]);
        Assert.Equal((Ahora, false), (i.FechaIntervencion, i.CerradaFirmada));
        Assert.Equal(i.Id, dto.Id);

        Assert.Equal(ObjectIdNuevo, _orden.IntervencionId);
        await _intervenciones.Received(1).GuardarAsync(Arg.Any<Intervencion>(), null, Arg.Any<CancellationToken>());
        await _ordenes.Received(1).GuardarAsync(null, Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _tecnico,
            AccionesAuditoria.Crear,
            GestionEjecucion.EntidadIntervencion,
            ObjectIdNuevo,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["orden"] == _orden.Id.ToString() && d["version_campos"] == "2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_CamposDinamicosInvalidos_ExplicaCadaCampo_YNoGuardaNada()
    {
        _orden.Iniciar(Ahora);

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.RegistrarIntervencionAsync(
            _orden.Id,
            Datos("""{"ciclos": 2.5, "resultado": "Tal vez", "presion": 120}"""),
            CancellationToken.None));

        Assert.Equal(
            ["camposDinamicos.ciclos", "camposDinamicos.presion", "camposDinamicos.resultado", "camposDinamicos.tasa_goteo"],
            error.Errores.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Tasa de goteo", error.Errores["camposDinamicos.tasa_goteo"][0]);
        await _intervenciones.DidNotReceive().GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.Null(_orden.IntervencionId);
    }

    [Fact]
    public async Task Registrar_CamposFijosInvalidos_Responde400()
    {
        _orden.Iniciar(Ahora);
        var datos = new DatosIntervencion(
            " ",
            null,
            [new DatosRepuesto("", 0, -1m), null],
            CostoManoObra: -5m,
            TiempoAtencionMin: 0,
            EstadoEquipoSalida: "Regular",
            CamposDinamicos: Json("""{"tasa_goteo": 10, "resultado": "Pasa"}"""));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.RegistrarIntervencionAsync(_orden.Id, datos, CancellationToken.None));

        Assert.Equal(
            ["accionesRealizadas", "costoManoObra", "diagnostico", "estadoEquipoSalida", "repuestos[0].cantidad", "repuestos[0].costoUnitario",
             "repuestos[0].nombre", "repuestos[1].cantidad", "repuestos[1].nombre", "tiempoAtencionMin"],
            error.Errores.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Registrar_SiMongoNoResponde_LaOrdenNoCambia()
    {
        _orden.Iniciar(Ahora);
        _intervenciones.GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorServicioNoDisponible(new TimeoutException()));

        await Assert.ThrowsAsync<ErrorServicioNoDisponible>(() => _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None));

        Assert.Null(_orden.IntervencionId);
        await _ordenes.DidNotReceive().GuardarAsync(Arg.Any<HistorialTiquete?>(), Arg.Any<CancellationToken>());
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Registrar_Correccion_ReemplazaElMismoDocumento_ConservaIdYFecha()
    {
        _orden.Iniciar(Ahora);
        _orden.Pausar();
        _orden.EnlazarIntervencion(ObjectIdNuevo);
        var anterior = new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _tecnico, TiposMantenimiento.Correctivo, "Antes", "Antes", [], null, 0m, null, null,
            new Dictionary<string, object>(), 1, Ahora.AddDays(-1), false);
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectIdNuevo, anterior));

        var dto = await _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None);

        Assert.Equal((anterior.Id, Ahora.AddDays(-1), (int?)2), (dto.Id, dto.FechaIntervencion, dto.VersionCampos));
        await _intervenciones.Received(1).GuardarAsync(Arg.Any<Intervencion>(), ObjectIdNuevo, Arg.Any<CancellationToken>());
        await _ordenes.DidNotReceive().GuardarAsync(Arg.Any<HistorialTiquete?>(), Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _tecnico, AccionesAuditoria.Actualizar, GestionEjecucion.EntidadIntervencion, ObjectIdNuevo,
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_SinIniciarLaOrden_Responde409()
    {
        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None));

        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, error.Codigo);
        await _intervenciones.DidNotReceive().GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_SobreUnaIntervencionFirmada_Responde409Rn004()
    {
        _orden.Iniciar(Ahora);
        var firmada = new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _tecnico, TiposMantenimiento.Correctivo, "D", "A", [], null, 0m, null, null,
            new Dictionary<string, object>(), 2, Ahora, true);
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectIdNuevo, firmada));

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None));

        Assert.Equal(GestionEjecucion.CodigoFirmada, error.Codigo);
        await _intervenciones.DidNotReceive().GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_TipoSinCamposDinamicos_GuardaSinVersionYRechazaClavesExtra()
    {
        _orden.Iniciar(Ahora);
        _configuracion.VersionesAsync(_tipo, Arg.Any<CancellationToken>()).Returns([]);

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.RegistrarIntervencionAsync(_orden.Id, Datos(), CancellationToken.None));
        Assert.Equal(["camposDinamicos.resultado", "camposDinamicos.tasa_goteo"], error.Errores.Keys.Order(StringComparer.Ordinal));

        await _caso.RegistrarIntervencionAsync(_orden.Id, Datos("{}"), CancellationToken.None);
        Assert.Equal(((int?)null, 0), (_guardada!.VersionCampos, _guardada.CamposDinamicos.Count));
    }

    // ---------- Consulta ----------

    [Fact]
    public async Task ObtenerIntervencion_ElTecnicoSoloLaDeSusOrdenes_Y404SiNoHay()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerIntervencionAsync(_orden.Id, CancellationToken.None));
        await _ordenes.Received(1).ObtenerAsync(_orden.Id, _tecnico, Arg.Any<CancellationToken>());

        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        var guardada = new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _tecnico, TiposMantenimiento.Correctivo, "D", "A", [], null, 0m, null, null,
            new Dictionary<string, object>(), 2, Ahora, false);
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectIdNuevo, guardada));

        Assert.Equal(guardada.Id, (await _caso.ObtenerIntervencionAsync(_orden.Id, CancellationToken.None)).Id);
        await _ordenes.Received(1).ObtenerAsync(_orden.Id, null, Arg.Any<CancellationToken>());
    }
}
