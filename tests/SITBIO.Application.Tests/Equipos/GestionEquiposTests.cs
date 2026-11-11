using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Tests.Equipos;

public class GestionEquiposTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // 06/11/2026 a las 03:00 UTC: en Colombia todavía es 05/11/2026.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 5);

    private readonly IRepositorioEquipos _repositorio = Substitute.For<IRepositorioEquipos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly TipoEquipo _tensiometro = new(Tenant, "Tensiómetro");
    private readonly ElementoCatalogo _ubicacion = new(Guid.NewGuid(), "Urgencias", true);
    private readonly GestionEquipos _caso;

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    public GestionEquiposTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(Guid.NewGuid());
        _tensiometro.DefinirValoresRegulatorios(ClasesRiesgo.I, true, 12);
        _repositorio.TipoEquipoAsync(_tensiometro.Id, Arg.Any<CancellationToken>()).Returns(_tensiometro);
        _repositorio.UbicacionAsync(_ubicacion.Id, Arg.Any<CancellationToken>()).Returns(_ubicacion);
        _repositorio.DuplicadosAsync(default!, default!, default, default).ReturnsForAnyArgs(new Duplicados(false, false));
        Capacidad(activos: 10);
        _caso = new GestionEquipos(_repositorio, _auditoria, _contexto, new TiempoFijo());
    }

    // Plan Consultorio: 300 equipos activos (DOC-01 Tabla 4).
    private void Capacidad(int activos) =>
        _repositorio.CapacidadEquiposAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadEquipos("Consultorio", 300, activos));

    private DatosEquipo Datos(
        string? clase = ClasesRiesgo.I,
        bool? calibracion = true,
        int? meses = 12,
        DatosPreventivo? preventivo = null) =>
        new(_tensiometro.Id, _ubicacion.Id, null, " SN-100 ", " PL-100 ", "Welch Allyn", "DS44", null,
            TiposAutorizacion.RegistroSanitario, "INVIMA 2019DM-0001", Hoy.AddYears(2), clase, calibracion, meses, preventivo);

    private static string[] ErroresDe(ErrorValidacion error, string campo) => error.Errores[campo];

    [Fact]
    public async Task Crear_DatosCompletos_GuardaOperativoYDevuelveElDto()
    {
        var dto = await _caso.CrearAsync(Datos(), CancellationToken.None);

        Assert.Equal("PL-100", dto.PlacaInventario);
        Assert.Equal("SN-100", dto.NumeroSerie);
        Assert.Equal(EstadosEquipo.CodigoOperativo, dto.Estado);
        Assert.Equal("Tensiómetro", dto.TipoEquipo);
        Assert.Equal("Urgencias", dto.Ubicacion);
        Assert.Equal(nameof(AvisoRegistro.Vigente), dto.AvisoRegistro);
        Assert.Null(dto.Preventivo);
        _repositorio.Received(1).Agregar(Arg.Is<EquipoBiomedico>(e => e.TenantId == Tenant && e.PeriodicidadCalibracionMeses == 12));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_GuardaElAltaComoPrimerRegistroDelHistorial()
    {
        var dto = await _caso.CrearAsync(Datos(), CancellationToken.None);

        _repositorio.Received(1).Agregar(Arg.Is<HistorialEstadoEquipo>(h =>
            h.EquipoId == dto.Id
            && h.EstadoAnteriorId == null
            && h.NuevoEstadoId == EstadosEquipo.Operativo
            && h.UsuarioId == _contexto.UsuarioId
            && h.Motivo == null));
    }

    [Fact]
    public async Task Crear_FaltanDatosRegulatorios_Responde400ConCadaCampo()
    {
        var datos = Datos() with { TipoAutorizacion = null, RegistroInvima = " ", VenceRegistro = null, ClaseRiesgo = "IV", RequiereCalibracion = null };

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(datos, CancellationToken.None));

        Assert.Equal(
            ["claseRiesgo", "registroInvima", "requiereCalibracion", "tipoAutorizacion", "venceRegistro"],
            error.Errores.Keys.Order());
        _repositorio.DidNotReceiveWithAnyArgs().Agregar(default(EquipoBiomedico)!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(null)]
    public async Task Crear_ConCalibracionYPeriodicidadFueraDeRango_Responde400(int? meses)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(Datos(meses: meses), CancellationToken.None));

        Assert.Single(ErroresDe(error, "periodicidadCalibracionMeses"));
    }

    [Fact]
    public async Task Crear_SinCalibracion_GuardaPeriodicidadVaciaYAnotaQueDifiereDelTipo()
    {
        var dto = await _caso.CrearAsync(Datos(calibracion: false, meses: 12), CancellationToken.None);

        Assert.False(dto.RequiereCalibracion);
        Assert.Null(dto.PeriodicidadCalibracionMeses);
        await _auditoria.Received(1).RegistrarAsync(
            _contexto.UsuarioId,
            AccionesAuditoria.Crear,
            GestionEquipos.Entidad,
            dto.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["placa"] == "PL-100" && d["difiere_del_tipo"] == "requiere_calibracion"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_IgualAlTipo_NoAnotaDiferencias()
    {
        await _caso.CrearAsync(Datos(), CancellationToken.None);

        await _auditoria.Received(1).RegistrarAsync(
            Arg.Any<Guid>(), AccionesAuditoria.Crear, GestionEquipos.Entidad, Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => !d.ContainsKey("difiere_del_tipo")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_TipoInexistenteOUbicacionInactiva_Responde400()
    {
        var inactiva = new ElementoCatalogo(Guid.NewGuid(), "Bodega", false);
        _repositorio.UbicacionAsync(inactiva.Id, Arg.Any<CancellationToken>()).Returns(inactiva);
        var datos = Datos() with { TipoEquipoId = Guid.NewGuid(), UbicacionId = inactiva.Id };

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(datos, CancellationToken.None));

        Assert.Equal("El tipo de equipo no existe.", Assert.Single(ErroresDe(error, "tipoEquipoId")));
        Assert.Equal("La ubicación está inactiva.", Assert.Single(ErroresDe(error, "ubicacionId")));
    }

    [Theory]
    [InlineData(true, false, "placa de inventario PL-100")]
    [InlineData(false, true, "número de serie SN-100")]
    public async Task Crear_PlacaOSerieRepetida_Responde409Rn001(bool placa, bool serie, string mensaje)
    {
        _repositorio.DuplicadosAsync("SN-100", "PL-100", null, Arg.Any<CancellationToken>()).Returns(new Duplicados(serie, placa));

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CrearAsync(Datos(), CancellationToken.None));

        Assert.Equal(GestionEquipos.CodigoDuplicado, error.Codigo);
        Assert.Contains(mensaje, error.Message);
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task Crear_ConElCupoDelPlanLleno_Responde409Rn009ConElConsumo()
    {
        Capacidad(activos: 300);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CrearAsync(Datos(), CancellationToken.None));

        Assert.Equal(GestionEquipos.CodigoLimitePlan, error.Codigo);
        Assert.Contains("Consultorio (300 de 300)", error.Message);
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task Actualizar_ConElCupoLleno_SiSePermite()
    {
        Capacidad(activos: 300);
        var leido = Existente();

        var dto = await _caso.ActualizarAsync(leido.Equipo.Id, Datos() with { Marca = "Omron" }, CancellationToken.None);

        Assert.Equal("Omron", dto.Marca);
        await _repositorio.DidNotReceive().CapacidadEquiposAsync(Arg.Any<CancellationToken>());
        await _repositorio.Received(1).DuplicadosAsync("SN-100", "PL-100", leido.Equipo.Id, Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            Arg.Any<Guid>(), AccionesAuditoria.Actualizar, GestionEquipos.Entidad, leido.Equipo.Id.ToString(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Actualizar_EquipoDeOtroTenant_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ActualizarAsync(Guid.NewGuid(), Datos(), CancellationToken.None));
    }

    [Fact]
    public async Task Actualizar_ConservaTipoInactivoQueYaTenia()
    {
        var antiguo = new TipoEquipo(Tenant, "Tipo retirado");
        typeof(TipoEquipo).GetProperty(nameof(TipoEquipo.EstadoActivo))!.SetValue(antiguo, false);
        _repositorio.TipoEquipoAsync(antiguo.Id, Arg.Any<CancellationToken>()).Returns(antiguo);
        var leido = Existente(antiguo.Id);

        var dto = await _caso.ActualizarAsync(leido.Equipo.Id, Datos() with { TipoEquipoId = antiguo.Id }, CancellationToken.None);

        Assert.Equal("Tipo retirado", dto.TipoEquipo);
    }

    [Fact]
    public async Task Preventivo_ConUltimo_CalculaElProximoYPuedeNacerVencido()
    {
        var ultima = Hoy.AddDays(-200);

        var dto = await _caso.CrearAsync(Datos(preventivo: new DatosPreventivo(180, ultima, null)), CancellationToken.None);

        Assert.NotNull(dto.Preventivo);
        Assert.Equal(ultima.AddDays(180), dto.Preventivo.ProximaFecha);
        Assert.Equal(ultima, dto.Preventivo.UltimaEjecucion);
        Assert.True(dto.Preventivo.Vencido);
        _repositorio.Received(1).Agregar(Arg.Is<PlanPreventivo>(p => p.Descripcion == GestionEquipos.DescripcionPreventivo));
    }

    [Fact]
    public async Task Preventivo_ProximoAjustado_SeRespeta()
    {
        var ultima = Hoy.AddDays(-30);

        var dto = await _caso.CrearAsync(Datos(preventivo: new DatosPreventivo(180, ultima, Hoy.AddDays(10))), CancellationToken.None);

        Assert.Equal(Hoy.AddDays(10), dto.Preventivo!.ProximaFecha);
        Assert.False(dto.Preventivo.Vencido);
    }

    [Theory]
    [InlineData(null, null, "preventivo.proximaFecha")]
    [InlineData(null, -1, "preventivo.proximaFecha")]
    [InlineData(1, null, "preventivo.ultimaEjecucion")]
    [InlineData(-10, -20, "preventivo.proximaFecha")]
    public async Task Preventivo_FechasNoValidas_Responde400(int? diasUltima, int? diasProxima, string campo)
    {
        var preventivo = new DatosPreventivo(
            90,
            diasUltima is { } u ? Hoy.AddDays(u) : null,
            diasProxima is { } p ? Hoy.AddDays(p) : null);

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(Datos(preventivo: preventivo), CancellationToken.None));

        Assert.Single(ErroresDe(error, campo));
    }

    [Fact]
    public async Task Preventivo_AlEditar_ReprogramaElPlanExistente()
    {
        var plan = new PlanPreventivo(Tenant, Guid.NewGuid(), "Semestral", 180, Hoy.AddDays(30), Ahora);
        var leido = Existente(plan: plan);

        var dto = await _caso.ActualizarAsync(leido.Equipo.Id, Datos(preventivo: new DatosPreventivo(90, Hoy.AddDays(-10), null)), CancellationToken.None);

        Assert.Equal(plan.Id, dto.Preventivo!.Id);
        Assert.Equal(Hoy.AddDays(80), plan.ProximaFecha);
        _repositorio.DidNotReceiveWithAnyArgs().Agregar(default(PlanPreventivo)!);
    }

    [Fact]
    public async Task Listar_ClaseOEstadoDesconocidos_Responde400()
    {
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroEquipos(Clase: "IV"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroEquipos(Estado: "ROTO"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(new FiltroEquipos(Tamano: 101), CancellationToken.None));
    }

    [Fact]
    public async Task Listar_NormalizaLosFiltrosAntesDeConsultar()
    {
        _repositorio.ListarAsync(default!, default).ReturnsForAnyArgs((Array.Empty<EquipoLeido>(), 0));

        await _caso.ListarAsync(new FiltroEquipos(" pl-1 ", "  ", " en_mantenimiento ", "IIa", false), CancellationToken.None);

        await _repositorio.Received(1).ListarAsync(
            new FiltroEquipos("pl-1", null, "EN_MANTENIMIENTO", "IIa", false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Obtener_CalculaElAvisoConLaFechaDeColombia()
    {
        // Vence el 05/11/2026: en Colombia es hoy, así que aún no está vencido.
        var leido = Existente(vence: Hoy);

        var dto = await _caso.ObtenerAsync(leido.Equipo.Id, CancellationToken.None);

        Assert.Equal(nameof(AvisoRegistro.PorVencer), dto.AvisoRegistro);
    }

    private EquipoLeido Existente(Guid? tipoId = null, PlanPreventivo? plan = null, DateOnly? vence = null)
    {
        var equipo = new EquipoBiomedico(Tenant, tipoId ?? _tensiometro.Id, _ubicacion.Id, "SN-100", "PL-100", "Welch Allyn", "DS44", Ahora);
        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", vence ?? Hoy.AddYears(1), ClasesRiesgo.I, true, 12);
        var leido = new EquipoLeido(equipo, "Tensiómetro", "Urgencias", null, EstadosEquipo.CodigoOperativo, EstadosEquipo.NombreOperativo, plan);
        _repositorio.ObtenerAsync(equipo.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(leido);
        return leido;
    }
}
