using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Mantenimiento;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Mantenimiento;

/// <summary>
/// Calibraciones (HU-022): la calibración completa queda en el historial del equipo con su vencimiento
/// (CA-HU-022-01), que es la fecha + la periodicidad del equipo si no se indica; un equipo que no requiere
/// calibración no se calibra (CA-HU-022-03, RN-010); el Técnico corrige mientras el Ingeniero no apruebe.
/// </summary>
public class GestionCalibracionesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // 13/11/2026 a las 10:00 en Colombia.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 13);

    private readonly IRepositorioCalibraciones _repositorio = Substitute.For<IRepositorioCalibraciones>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Guid _laboratorio = Guid.NewGuid();
    private readonly EquipoDeCalibracion _equipo = new(Guid.NewGuid(), "MON-07", false, true, 12);
    private readonly GestionCalibraciones _caso;
    private RegistroCalibracion? _agregada;

    public GestionCalibracionesTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_tecnico);
        _contexto.Rol.Returns(CodigosRol.TecnicoMantenimiento);
        _repositorio.EquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(_equipo);
        _repositorio.ExisteProveedorAsync(_laboratorio, Arg.Any<CancellationToken>()).Returns(true);
        _repositorio.When(r => r.Agregar(Arg.Any<RegistroCalibracion>())).Do(llamada =>
        {
            _agregada = llamada.Arg<RegistroCalibracion>();
            _repositorio.ObtenerAsync(_agregada.Id, Arg.Any<CancellationToken>()).Returns(Leida(_agregada));
        });
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionCalibraciones(_repositorio, _auditoria, _contexto, tiempo);
    }

    private static CalibracionLeida Leida(RegistroCalibracion calibracion) =>
        new(calibracion, "MON-07", "Monitor", "Urgencias", null, "Ana Mora", calibracion.Aprobada ? "Luis Díaz" : null);

    private RegistroCalibracion Existente(bool aprobada = false)
    {
        var calibracion = new RegistroCalibracion(Tenant, _equipo.Id, _tecnico, Hoy.AddDays(-10), "Analizador", RegistroCalibracion.Conforme, Hoy.AddDays(-10).AddMonths(12));
        if (aprobada)
        {
            calibracion.Aprobar(Guid.NewGuid());
        }

        _repositorio.CalibracionAsync(calibracion.Id, Arg.Any<CancellationToken>()).Returns(calibracion);
        _repositorio.ObtenerAsync(calibracion.Id, Arg.Any<CancellationToken>()).Returns(_ => Leida(calibracion));
        return calibracion;
    }

    [Fact]
    public async Task Registrar_SinVencimiento_LoCalculaConLaPeriodicidad_YQuedaEnElHistorialSinAprobar()
    {
        var registrada = await _caso.RegistrarAsync(
            new DatosCalibracion(_equipo.Id, Hoy.AddDays(-2), " Analizador Fluke IDA-5 ", null, " CERT-001 ", null, _laboratorio),
            CancellationToken.None);

        Assert.NotNull(_agregada);
        Assert.Equal((_equipo.Id, _tecnico, Tenant, _laboratorio), (_agregada.EquipoId, _agregada.RegistradoPorId, _agregada.TenantId, _agregada.ProveedorId!.Value));
        Assert.Equal((Hoy.AddDays(-2), new DateOnly(2027, 11, 11), "Analizador Fluke IDA-5", RegistroCalibracion.Conforme, "CERT-001"), (registrada.FechaCalibracion, registrada.FechaVencimiento, registrada.PatronReferencia, registrada.Resultado, registrada.NumeroCertificado));
        Assert.Equal((true, false, "MON-07"), (registrada.Vigente, registrada.Aprobada, registrada.PlacaInventario));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _tecnico,
            AccionesAuditoria.Crear,
            GestionCalibraciones.Entidad,
            _agregada.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["equipo"] == "MON-07" && d["fechaVencimiento"] == "2027-11-11"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registrar_ConElVencimientoDelCertificado_LoRespeta_YLaVencidaNoEstaVigente()
    {
        var registrada = await _caso.RegistrarAsync(
            new DatosCalibracion(_equipo.Id, Hoy.AddMonths(-7), "Simulador", "No Conforme", null, Hoy.AddDays(-1), null),
            CancellationToken.None);

        Assert.Equal((Hoy.AddDays(-1), RegistroCalibracion.NoConforme, false), (registrada.FechaVencimiento, registrada.Resultado, registrada.Vigente));
    }

    [Fact]
    public async Task Registrar_EquipoQueNoRequiereCalibracion_SeRechaza_RN010()
    {
        var estetoscopio = new EquipoDeCalibracion(Guid.NewGuid(), "EST-01", false, false, null);
        _repositorio.EquipoAsync(estetoscopio.Id, Arg.Any<CancellationToken>()).Returns(estetoscopio);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.RegistrarAsync(new DatosCalibracion(estetoscopio.Id, Hoy, "Analizador", null, null, null, null), CancellationToken.None));

        Assert.Equal(GestionCalibraciones.CodigoNoRequiere, error.Codigo);
        _repositorio.DidNotReceive().Agregar(Arg.Any<RegistroCalibracion>());
    }

    [Fact]
    public async Task Registrar_EquipoDeBaja_OInexistente_SeRechaza()
    {
        var deBaja = new EquipoDeCalibracion(Guid.NewGuid(), "MON-99", true, true, 12);
        _repositorio.EquipoAsync(deBaja.Id, Arg.Any<CancellationToken>()).Returns(deBaja);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.RegistrarAsync(new DatosCalibracion(deBaja.Id, Hoy, "Analizador", null, null, null, null), CancellationToken.None));
        Assert.Equal(GestionCalibraciones.CodigoEquipoDeBaja, error.Codigo);

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            _caso.RegistrarAsync(new DatosCalibracion(Guid.NewGuid(), Hoy, "Analizador", null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Registrar_ConDatosInvalidos_DevuelveLosErroresJuntoACadaCampo()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.RegistrarAsync(
            new DatosCalibracion(null, Hoy.AddDays(1), " ", "conforme con observaciones", new string('C', 81), null, null),
            CancellationToken.None));

        Assert.Equal(
            ["equipoId", "fechaCalibracion", "numeroCertificado", "patronReferencia", "resultado"],
            error.Errores.Keys.Order(StringComparer.Ordinal));
        _repositorio.DidNotReceive().Agregar(Arg.Any<RegistroCalibracion>());
    }

    [Fact]
    public async Task Registrar_VencimientoAnterior_OLaboratorioAjeno_SeRechazan()
    {
        var vencimiento = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.RegistrarAsync(new DatosCalibracion(_equipo.Id, Hoy, "Analizador", null, null, Hoy.AddDays(-1), null), CancellationToken.None));
        Assert.Contains("fechaVencimiento", vencimiento.Errores.Keys);

        var laboratorio = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.RegistrarAsync(new DatosCalibracion(_equipo.Id, Hoy, "Analizador", null, null, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("proveedorId", laboratorio.Errores.Keys);
    }

    [Fact]
    public async Task Corregir_CambiarLaFecha_RecalculaElVencimiento_YLoDemasNoCambia()
    {
        var calibracion = Existente();

        var corregida = await _caso.CorregirAsync(calibracion.Id, new DatosCorreccionCalibracion(Hoy.AddDays(-5), null, null, "CERT-2", null, null), CancellationToken.None);

        Assert.Equal((Hoy.AddDays(-5), Hoy.AddDays(-5).AddMonths(12), "Analizador", "CERT-2"), (corregida.FechaCalibracion, corregida.FechaVencimiento, corregida.PatronReferencia, corregida.NumeroCertificado));

        // Sin fecha nueva, el vencimiento se conserva.
        await _caso.CorregirAsync(calibracion.Id, new DatosCorreccionCalibracion(null, "Simulador", null, null, null, null), CancellationToken.None);
        Assert.Equal(("Simulador", Hoy.AddDays(-5).AddMonths(12)), (calibracion.PatronReferencia, calibracion.FechaVencimiento));
        await _auditoria.Received(2).RegistrarAsync(_tecnico, AccionesAuditoria.Actualizar, GestionCalibraciones.Entidad, calibracion.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprobar_LaDejaAprobada_YDespuesNoSeCorrigeNiSeApruebaDeNuevo()
    {
        var ingeniero = Guid.NewGuid();
        _contexto.UsuarioId.Returns(ingeniero);
        var calibracion = Existente();

        var aprobada = await _caso.AprobarAsync(calibracion.Id, CancellationToken.None);

        Assert.Equal((true, (Guid?)ingeniero, "Luis Díaz"), (aprobada.Aprobada, aprobada.AprobadoPorId, aprobada.AprobadoPor));
        var otra = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.AprobarAsync(calibracion.Id, CancellationToken.None));
        Assert.Equal(GestionCalibraciones.CodigoAprobada, otra.Codigo);
        await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.CorregirAsync(calibracion.Id, new DatosCorreccionCalibracion(null, "Otro", null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Listar_ValidaLaPagina_YMarcaLaVigencia()
    {
        var vigente = Existente();
        _repositorio.ListarAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>()).Returns(([Leida(vigente)], 1));

        var pagina = await _caso.ListarAsync(new FiltroCalibraciones(_equipo.Id), CancellationToken.None);

        Assert.Equal((1, true), (pagina.Total, Assert.Single(pagina.Items).Vigente));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroCalibraciones(Pagina: 0), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
