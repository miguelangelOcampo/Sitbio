using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Ordenes;

/// <summary>Asignación y consulta de órdenes de trabajo (HU-004): validación, RN-003, tiquete «asignado», bitácora y alcance del técnico.</summary>
public class GestionOrdenesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // 13/11/2026 23:30 en Colombia = 14/11/2026 04:30 UTC: «hoy» es el 13 en Colombia.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 4, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 13);

    private readonly IRepositorioOrdenes _repositorio = Substitute.For<IRepositorioOrdenes>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _ingeniero = Guid.NewGuid();
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Tiquete _tiquete = new(Tenant, Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, Criticidades.Critica, Ahora.AddHours(-1));
    private readonly GestionOrdenes _caso;

    // Lo que la persistencia hizo en RegistrarAsync.
    private OrdenTrabajo? _guardada;
    private HistorialTiquete? _cambio;

    public GestionOrdenesTests()
    {
        _tiquete.Numerar(15);
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_ingeniero);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _repositorio.TiqueteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Tiquete?)null);
        _repositorio.TiqueteAsync(_tiquete.Id, Arg.Any<CancellationToken>()).Returns(_tiquete);
        _repositorio.TecnicoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TecnicoCandidato?)null);
        _repositorio.TecnicoAsync(_tecnico, Arg.Any<CancellationToken>()).Returns(new TecnicoCandidato(_tecnico, CodigosRol.TecnicoMantenimiento, true));
        _repositorio.RegistrarAsync(Arg.Any<OrdenTrabajo>(), Arg.Any<HistorialTiquete>(), Arg.Any<CancellationToken>())
            .Returns(llamada =>
            {
                _guardada = llamada.ArgAt<OrdenTrabajo>(0);
                _cambio = llamada.ArgAt<HistorialTiquete>(1);
                return Task.CompletedTask;
            });
        _repositorio.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(llamada => _guardada is { } o && o.Id == llamada.ArgAt<Guid>(0) ? Leida(o) : null);
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionOrdenes(_repositorio, _auditoria, _contexto, tiempo);
    }

    private OrdenLeida Leida(OrdenTrabajo orden) =>
        new(orden, "PL-001", "SN-1", "Monitor", "Urgencias", "Carlos Ruiz", _tiquete.Codigo, _tiquete.TipoFalla);

    private DatosOrden Datos(Guid? tecnico = null, string? prioridad = null, DateOnly? programada = null) =>
        new(_tiquete.Id, tecnico ?? _tecnico, prioridad, programada);

    // ---------- CA-HU-004-01 ----------

    [Fact]
    public async Task Asignar_CreaLaOrdenDelTecnicoYElTiquetePasaAAsignado()
    {
        var creada = await _caso.AsignarAsync(Datos(programada: Hoy), CancellationToken.None);

        Assert.Equal((_tecnico, "Carlos Ruiz", (Guid?)_ingeniero), (creada.TecnicoId, creada.Tecnico, creada.AsignadoPorId));
        Assert.Equal((EstadosOrden.Asignada, TiposMantenimiento.Correctivo, Criticidades.Critica), (creada.Estado, creada.TipoMantenimiento, creada.Prioridad));
        Assert.Equal(("TQT-000015", (DateOnly?)Hoy, (DateTimeOffset?)Ahora), (creada.CodigoTiquete, creada.FechaProgramada, creada.FechaAsignacion));
        Assert.Equal((Tenant, (Guid?)_tiquete.Id), (_guardada!.TenantId, _guardada.TiqueteId));
        Assert.Equal(EstadosTiquete.Asignado, _tiquete.Estado);
        Assert.Equal((EstadosTiquete.Recibido, EstadosTiquete.Asignado, _ingeniero), (_cambio!.EstadoAnterior, _cambio.NuevoEstado, _cambio.UsuarioId));
    }

    [Fact]
    public async Task Asignar_ConOtraPrioridad_LaUsaEnLugarDeLaSugerida()
    {
        var creada = await _caso.AsignarAsync(Datos(prioridad: " ALTA "), CancellationToken.None);

        Assert.Equal(Criticidades.Alta, creada.Prioridad);
    }

    [Fact]
    public async Task Asignar_DejaLaOrdenEnLaBitacoraSinDatosPersonales()
    {
        await _caso.AsignarAsync(Datos(), CancellationToken.None);

        await _auditoria.Received(1).RegistrarAsync(
            _ingeniero,
            AccionesAuditoria.Crear,
            GestionOrdenes.Entidad,
            _guardada!.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["tiquete"] == "TQT-000015" && d["prioridad"] == Criticidades.Critica && d["tipo"] == TiposMantenimiento.Correctivo && d.Count == 3),
            Arg.Any<CancellationToken>());
    }

    // ---------- CA-HU-004-02 (RN-003) ----------

    [Theory]
    [InlineData(CodigosRol.IngenieroBiomedico, true)]
    [InlineData(CodigosRol.PersonalClinico, true)]
    [InlineData(CodigosRol.TecnicoMantenimiento, false)]
    public async Task Asignar_AQuienNoEsTecnicoActivo_SeRechazaSinTocarElTiquete(string rol, bool activo)
    {
        var otro = Guid.NewGuid();
        _repositorio.TecnicoAsync(otro, Arg.Any<CancellationToken>()).Returns(new TecnicoCandidato(otro, rol, activo));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.AsignarAsync(Datos(otro), CancellationToken.None));

        Assert.Equal(GestionOrdenes.MensajeRn003, error.Errores["tecnicoId"].Single());
        Assert.Equal(EstadosTiquete.Recibido, _tiquete.Estado);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Asignar_AUnUsuarioDeOtraInstitucion_SeRechazaComoRn003()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.AsignarAsync(Datos(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(GestionOrdenes.MensajeRn003, error.Errores["tecnicoId"].Single());
    }

    // ---------- Validaciones ----------

    [Fact]
    public async Task Asignar_SinDatos_DevuelveTodosLosErroresJuntos()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.AsignarAsync(new DatosOrden(null, Guid.Empty, "urgente", Hoy.AddDays(-1)), CancellationToken.None));

        Assert.Equal(["fechaProgramada", "prioridad", "tecnicoId", "tiqueteId"], error.Errores.Keys.Order());
        Assert.Equal("Selecciona el tiquete que vas a asignar.", error.Errores["tiqueteId"].Single());
    }

    [Fact]
    public async Task Asignar_TiqueteDeOtraInstitucion_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.AsignarAsync(Datos() with { TiqueteId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task Asignar_TiqueteYaAsignado_Responde409()
    {
        await _caso.AsignarAsync(Datos(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.AsignarAsync(Datos(), CancellationToken.None));

        Assert.Equal((GestionOrdenes.CodigoTiqueteAsignado, "El tiquete TQT-000015 ya tiene una orden de trabajo."), (error.Codigo, error.Message));
        await _repositorio.Received(1).RegistrarAsync(Arg.Any<OrdenTrabajo>(), Arg.Any<HistorialTiquete>(), Arg.Any<CancellationToken>());
    }

    // ---------- Consulta: el técnico ve las suyas ----------

    [Theory]
    [InlineData(CodigosRol.TecnicoMantenimiento, false, true)]
    [InlineData(CodigosRol.TecnicoMantenimiento, true, true)]
    [InlineData(CodigosRol.IngenieroBiomedico, false, false)]
    [InlineData(CodigosRol.IngenieroBiomedico, true, true)]
    [InlineData(CodigosRol.EnteRegulador, false, false)]
    public async Task Listar_ElTecnicoSoloVeLasSuyasYLosDemasConAsignadasAMi(string rol, bool asignadasAMi, bool soloDelUsuario)
    {
        _contexto.Rol.Returns(rol);
        _repositorio.ListarAsync(Arg.Any<FiltroOrdenes>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(([], 0));

        await _caso.ListarAsync(new FiltroOrdenes(" Asignada ", asignadasAMi), CancellationToken.None);

        await _repositorio.Received(1).ListarAsync(
            new FiltroOrdenes(EstadosOrden.Asignada, asignadasAMi), soloDelUsuario ? _ingeniero : null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listar_EstadoInvalidoOTamanoFueraDeRango_Responde400()
    {
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroOrdenes("abierta"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroOrdenes(null, Tamano: 101), CancellationToken.None));
    }

    [Fact]
    public async Task Obtener_DeOtroTecnico_Responde404()
    {
        _contexto.Rol.Returns(CodigosRol.TecnicoMantenimiento);

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));

        await _repositorio.Received(1).ObtenerAsync(Arg.Any<Guid>(), _ingeniero, Arg.Any<CancellationToken>());
    }
}
