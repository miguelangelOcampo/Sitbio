using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Application.Tiquetes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Tiquetes;

/// <summary>Reporte y consulta de tiquetes (HU-003, HU-018): validación, código, RN-005, bitácora y alcance de AC-04.</summary>
public class GestionTiquetesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioTiquetes _repositorio = Substitute.For<IRepositorioTiquetes>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly EquipoBiomedico _monitor = new(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SN-1", "PL-001", "Mindray", "uMEC10", Ahora.AddYears(-1));
    private readonly GestionTiquetes _caso;

    // Lo que la persistencia hizo en RegistrarAsync.
    private Tiquete? _guardado;
    private HistorialTiquete? _recepcion;
    private HistorialEstadoEquipo? _cambioEquipo;

    public GestionTiquetesTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_usuario);
        _contexto.Rol.Returns(CodigosRol.PersonalClinico);
        _repositorio.EquipoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EquipoBiomedico?)null);
        _repositorio.EquipoAsync(_monitor.Id, Arg.Any<CancellationToken>()).Returns(_monitor);
        _repositorio.TipoFallaActivoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _repositorio.TipoFallaActivoAsync("no enciende", Arg.Any<CancellationToken>()).Returns("No enciende");
        _repositorio.TipoFallaActivoAsync("No enciende", Arg.Any<CancellationToken>()).Returns("No enciende");
        _repositorio.RegistrarAsync(Arg.Any<Tiquete>(), Arg.Any<HistorialTiquete>(), Arg.Any<HistorialEstadoEquipo?>(), Arg.Any<CancellationToken>())
            .Returns(llamada =>
            {
                _guardado = llamada.ArgAt<Tiquete>(0);
                _recepcion = llamada.ArgAt<HistorialTiquete>(1);
                _cambioEquipo = llamada.ArgAt<HistorialEstadoEquipo?>(2);
                _guardado.Numerar(15);
                return Task.CompletedTask;
            });
        _repositorio.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(llamada => _guardado is { } t && t.Codigo == llamada.ArgAt<string>(0) ? Leido(t) : null);
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionTiquetes(_repositorio, _auditoria, _contexto, tiempo);
    }

    private TiqueteLeido Leido(Tiquete tiquete) =>
        new(tiquete, _monitor.PlacaInventario, _monitor.NumeroSerie, "Monitor", "Urgencias", "Laura Gómez",
            EstadosEquipo.Codigos[_monitor.EstadoEquipoId - 1]);

    private DatosTiquete Datos(string? criticidad = Criticidades.Alta, Guid? equipoId = null) =>
        new(equipoId ?? _monitor.Id, "no enciende", "  La pantalla queda en negro al conectarlo.  ", criticidad);

    // ---------- CA-HU-003-01: código único ----------

    [Fact]
    public async Task Reportar_GeneraElCodigoYQuedaRecibidoANombreDeQuienReporta()
    {
        var creado = await _caso.ReportarAsync(Datos(), CancellationToken.None);

        Assert.Equal(("TQT-000015", EstadosTiquete.Recibido), (creado.Codigo, creado.Estado));
        Assert.Equal((_usuario, Tenant), (_guardado!.ReportanteId, _guardado.TenantId));
        Assert.Equal(("No enciende", "La pantalla queda en negro al conectarlo."), (creado.TipoFalla, creado.Descripcion));
        Assert.Equal((Ahora, "PL-001"), (creado.FechaReporte, creado.PlacaInventario));
        Assert.Equal((_guardado.Id, EstadosTiquete.Recibido), (_recepcion!.TiqueteId, _recepcion.NuevoEstado));
    }

    [Fact]
    public async Task Reportar_DejaElTiqueteEnLaBitacora()
    {
        var creado = await _caso.ReportarAsync(Datos(), CancellationToken.None);

        await _auditoria.Received(1).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Crear,
            GestionTiquetes.Entidad,
            creado.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["codigo"] == "TQT-000015" && d["criticidad"] == Criticidades.Alta && d["placa"] == "PL-001"),
            Arg.Any<CancellationToken>());
        await _auditoria.DidNotReceive().RegistrarAsync(
            Arg.Any<Guid>(), AccionesAuditoria.Actualizar, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    // ---------- CA-HU-003-02: validación en español, sin tiquete ----------

    [Fact]
    public async Task Reportar_SinEquipo_NoGeneraTiquete()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.ReportarAsync(new DatosTiquete(null, "No enciende", "Falla", Criticidades.Baja), CancellationToken.None));

        Assert.Equal(["Selecciona el equipo que presenta la falla."], error.Errores["equipoId"]);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Reportar_ConVariosErrores_LosDevuelveTodosJuntos()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.ReportarAsync(new DatosTiquete(Guid.Empty, " ", null, "urgente"), CancellationToken.None));

        Assert.Equal(["equipoId", "tipoFalla", "criticidad"], error.Errores.Keys);
        Assert.Equal(["Elige la criticidad: crítica, alta, media o baja."], error.Errores["criticidad"]);
    }

    [Fact]
    public async Task Reportar_TipoDeFallaFueraDelCatalogo_SeRechaza()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.ReportarAsync(Datos() with { TipoFalla = "Se cayó" }, CancellationToken.None));

        Assert.Equal(["Ese tipo de falla no está activo en el catálogo de tu institución."], error.Errores["tipoFalla"]);
    }

    [Fact]
    public async Task Reportar_SinDescripcion_SeAcepta()
    {
        var creado = await _caso.ReportarAsync(Datos() with { Descripcion = null }, CancellationToken.None);

        Assert.Equal(string.Empty, creado.Descripcion);
    }

    [Fact]
    public async Task TiposFalla_SonLosActivosDelCatalogo()
    {
        _repositorio.TiposFallaActivosAsync(Arg.Any<CancellationToken>()).Returns(["Alarma", "No enciende"]);

        Assert.Equal(["Alarma", "No enciende"], await _caso.TiposFallaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reportar_DescripcionMuyLarga_SeRechaza()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.ReportarAsync(Datos() with { Descripcion = new string('x', Tiquete.LongitudMaximaDescripcion + 1) }, CancellationToken.None));

        Assert.Contains("descripcion", error.Errores.Keys);
    }

    [Fact]
    public async Task Reportar_EquipoDeOtraInstitucionOInexistente_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ReportarAsync(Datos(equipoId: Guid.NewGuid()), CancellationToken.None));
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Reportar_EquipoDadoDeBaja_SeRechaza()
    {
        _monitor.DarDeBaja("Obsoleto", Guid.NewGuid(), Ahora.AddDays(-1));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ReportarAsync(Datos(), CancellationToken.None));

        Assert.Equal(["El equipo está dado de baja; no admite reportes de falla."], error.Errores["equipoId"]);
    }

    // ---------- CA-HU-018: criticidad y RN-005 ----------

    [Theory]
    [InlineData(Criticidades.Alta)]
    [InlineData(Criticidades.Media)]
    [InlineData(Criticidades.Baja)]
    public async Task Reportar_NoCritica_GuardaLaCriticidadYNoCambiaElEquipo(string criticidad)
    {
        var creado = await _caso.ReportarAsync(Datos(criticidad.ToUpperInvariant()), CancellationToken.None);

        Assert.Equal((criticidad, criticidad), (creado.Criticidad, creado.PrioridadSugerida));
        Assert.Null(_cambioEquipo);
        Assert.Equal(EstadosEquipo.Operativo, _monitor.EstadoEquipoId);
        Assert.False(creado.EquipoFueraDeServicio);
    }

    [Fact]
    public async Task Reportar_Critica_PasaElEquipoAFueraDeServicioEnLaMismaTransaccion()
    {
        var creado = await _caso.ReportarAsync(Datos(Criticidades.Critica), CancellationToken.None);

        Assert.Equal(EstadosEquipo.FueraDeServicio, _monitor.EstadoEquipoId);
        Assert.NotNull(_cambioEquipo);
        Assert.Equal((EstadosEquipo.Operativo, EstadosEquipo.FueraDeServicio), (_cambioEquipo!.EstadoAnteriorId, _cambioEquipo.NuevoEstadoId));
        Assert.Equal((_usuario, Ahora), (_cambioEquipo.UsuarioId, _cambioEquipo.FechaCambio));
        Assert.Equal("Falla crítica reportada (RN-005): No enciende", _cambioEquipo.Motivo);
        Assert.True(creado.EquipoFueraDeServicio);
        Assert.Equal(Criticidades.Critica, creado.PrioridadSugerida);
        await _auditoria.Received(1).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Actualizar,
            GestionEquipos.Entidad,
            _monitor.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["estado"] == "FUERA_DE_SERVICIO" && d["tiquete"] == "TQT-000015" && d["regla"] == "RN-005"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reportar_CriticaConEquipoYaFueraDeServicio_NoRepiteElCambio()
    {
        _monitor.CambiarEstado(EstadosEquipo.FueraDeServicio, "Falla anterior", Guid.NewGuid(), Ahora.AddDays(-2));

        await _caso.ReportarAsync(Datos(Criticidades.Critica), CancellationToken.None);

        Assert.Null(_cambioEquipo);
        Assert.Equal(EstadosEquipo.FueraDeServicio, _monitor.EstadoEquipoId);
    }

    // ---------- Consulta: el Personal Clínico solo ve lo suyo ----------

    [Theory]
    [InlineData(CodigosRol.PersonalClinico, true)]
    [InlineData(CodigosRol.IngenieroBiomedico, false)]
    [InlineData(CodigosRol.TecnicoMantenimiento, false)]
    public async Task Listar_PersonalClinico_SoloConsultaLosSuyos(string rol, bool soloPropios)
    {
        _contexto.Rol.Returns(rol);
        _repositorio.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(([], 0));

        await _caso.ListarAsync(new FiltroTiquetes(" Recibido ", "CRITICA"), CancellationToken.None);

        await _repositorio.Received(1).ListarAsync(
            new FiltroTiquetes(EstadosTiquete.Recibido, Criticidades.Critica), soloPropios ? _usuario : null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abierto", null, "estado")]
    [InlineData(null, "urgente", "criticidad")]
    public async Task Listar_FiltroInvalido_Responde400(string? estado, string? criticidad, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroTiquetes(estado, criticidad), CancellationToken.None));

        Assert.Contains(campo, error.Errores.Keys);
    }

    [Fact]
    public async Task Listar_TamanoFueraDeRango_Responde400() =>
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroTiquetes(null, null, 1, 101), CancellationToken.None));

    [Fact]
    public async Task Obtener_NormalizaElCodigoYAplicaElAlcanceDelRol()
    {
        var tiquete = new Tiquete(Tenant, _monitor.Id, _usuario, "No enciende", "Falla", Criticidades.Baja, Ahora);
        tiquete.Numerar(3);
        _repositorio.ObtenerAsync("TQT-000003", _usuario, Arg.Any<CancellationToken>()).Returns(Leido(tiquete));

        var leido = await _caso.ObtenerAsync(" tqt-000003 ", CancellationToken.None);

        Assert.Equal("TQT-000003", leido.Codigo);
    }

    [Theory]
    [InlineData("TQT-000099")]
    [InlineData("cualquier-cosa")]
    public async Task Obtener_InexistenteOConFormatoInvalido_Responde404(string codigo) =>
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(codigo, CancellationToken.None));
}
