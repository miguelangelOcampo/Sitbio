using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Tests.Equipos;

/// <summary>Cambio de estado, baja lógica y reversión (HU-014, RF-014; RN-009 al revertir).</summary>
public class CambioEstadoEquipoTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ingeniero = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioEquipos _repositorio = Substitute.For<IRepositorioEquipos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly EquipoBiomedico _equipo = new(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SN-200", "PL-200", "Welch Allyn", "DS44", Ahora.AddYears(-2));
    private readonly GestionEquipos _caso;

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    public CambioEstadoEquipoTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(Ingeniero);
        Capacidad(activos: 10);

        // Cada lectura refleja el estado actual del equipo, como lo haría la base.
        _repositorio.ObtenerAsync(_equipo.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => new EquipoLeido(
                _equipo, "Tensiómetro", "Urgencias", null, EstadosEquipo.Codigos[_equipo.EstadoEquipoId - 1], Nombre(_equipo.EstadoEquipoId), null));
        _caso = new GestionEquipos(_repositorio, _auditoria, _contexto, new TiempoFijo());
    }

    private static string Nombre(short estado) => estado switch
    {
        EstadosEquipo.Operativo => "Operativo",
        EstadosEquipo.EnMantenimiento => "En mantenimiento",
        EstadosEquipo.FueraDeServicio => "Fuera de servicio",
        _ => "Baja",
    };

    private void Capacidad(int activos) =>
        _repositorio.CapacidadEquiposAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadEquipos("Consultorio", 300, activos));

    private Task<EquipoDto> Cambiar(string? estado, string? motivo = "Motivo de prueba") =>
        _caso.CambiarEstadoAsync(_equipo.Id, new DatosCambioEstado(estado, motivo), CancellationToken.None);

    private void DarDeBajaAntes() => _equipo.DarDeBaja("Obsoleto", Ingeniero, Ahora.AddDays(-30));

    [Fact]
    public async Task CA_HU_014_01_CambioConMotivo_GuardaElHistorialConFechaYUsuarioDelToken()
    {
        var dto = await Cambiar(" fuera_de_servicio ", "Falla en la fuente de poder.");

        Assert.Equal(("FUERA_DE_SERVICIO", "Fuera de servicio"), (dto.Estado, dto.EstadoNombre));
        _repositorio.Received(1).Agregar(Arg.Is<HistorialEstadoEquipo>(h =>
            h.EquipoId == _equipo.Id
            && h.EstadoAnteriorId == EstadosEquipo.Operativo
            && h.NuevoEstadoId == EstadosEquipo.FueraDeServicio
            && h.Motivo == "Falla en la fuente de poder."
            && h.UsuarioId == Ingeniero
            && h.FechaCambio == Ahora));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CambioDeEstado_QuedaEnLaBitacoraSinElMotivo()
    {
        await Cambiar("EN_MANTENIMIENTO", "Paciente Juan Pérez reportó ruido.");

        await _auditoria.Received(1).RegistrarAsync(
            Ingeniero,
            AccionesAuditoria.Actualizar,
            GestionEquipos.Entidad,
            _equipo.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["placa"] == "PL-200"
                && d["cambio"] == "estado"
                && d["estado_anterior"] == "OPERATIVO"
                && d["estado_nuevo"] == "EN_MANTENIMIENTO"
                && !d.Values.Any(v => v.Contains("Pérez"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CA_HU_014_02_Baja_DejaDeEstarActivoYConservaLaHojaDeVida()
    {
        var dto = await Cambiar("BAJA", "Obsoleto, sin repuestos del fabricante.");

        Assert.Equal("BAJA", dto.Estado);
        Assert.Equal(Ahora, dto.FechaBaja);
        Assert.Equal(("PL-200", "SN-200"), (dto.PlacaInventario, dto.NumeroSerie));
        await _auditoria.Received(1).RegistrarAsync(
            Ingeniero, AccionesAuditoria.Actualizar, GestionEquipos.Entidad, _equipo.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["cambio"] == "baja"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reversion_PedirOperativoParaUnEquipoDeBaja_LoReactiva()
    {
        DarDeBajaAntes();

        var dto = await Cambiar("OPERATIVO", "Se consiguieron los repuestos.");

        Assert.Equal("OPERATIVO", dto.Estado);
        Assert.Null(dto.FechaBaja);
        _repositorio.Received(1).Agregar(Arg.Is<HistorialEstadoEquipo>(h =>
            h.EstadoAnteriorId == EstadosEquipo.Baja && h.NuevoEstadoId == EstadosEquipo.Operativo));
        await _auditoria.Received(1).RegistrarAsync(
            Ingeniero, AccionesAuditoria.Actualizar, GestionEquipos.Entidad, _equipo.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["cambio"] == "reversion_baja"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reversion_ConElCupoDelPlanLleno_Responde409Rn009YSigueDeBaja()
    {
        DarDeBajaAntes();
        Capacidad(activos: 300);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Cambiar("OPERATIVO"));

        Assert.Equal(GestionEquipos.CodigoLimitePlan, error.Codigo);
        Assert.True(_equipo.DadoDeBaja);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Baja_ConElCupoLleno_SiSePermite()
    {
        Capacidad(activos: 300);

        var dto = await Cambiar("BAJA");

        Assert.Equal("BAJA", dto.Estado);
    }

    [Theory]
    [InlineData("EN_MANTENIMIENTO")]
    [InlineData("BAJA")]
    public async Task EquipoDeBaja_HaciaOtroEstadoQueNoSeaOperativo_Responde409Eq002(string estado)
    {
        DarDeBajaAntes();

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Cambiar(estado));

        Assert.Equal(GestionEquipos.CodigoBajaSoloOperativo, error.Codigo);
    }

    [Fact]
    public async Task MismoEstado_Responde409Eq001()
    {
        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Cambiar("OPERATIVO"));

        Assert.Equal(GestionEquipos.CodigoMismoEstado, error.Codigo);
        Assert.Contains("Operativo", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("REPARADO")]
    public async Task EstadoVacioOInexistente_Responde400EnEstado(string? estado)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar(estado));

        Assert.Equal(["estado"], error.Errores.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(301)]
    public async Task MotivoVacioOLargo_Responde400EnMotivoSinGuardar(object? motivo)
    {
        var texto = motivo is int largo ? new string('m', largo) : (string?)motivo;

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar("FUERA_DE_SERVICIO", texto));

        Assert.Equal(["motivo"], error.Errores.Keys);
        Assert.Equal(EstadosEquipo.Operativo, _equipo.EstadoEquipoId);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EquipoDeOtroTenant_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            _caso.CambiarEstadoAsync(Guid.NewGuid(), new DatosCambioEstado("BAJA", "Motivo"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.HistorialAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Historial_DevuelveCadaCambioConSusNombres()
    {
        var baja = _equipo.DarDeBaja("Obsoleto", Ingeniero, Ahora);
        _repositorio.HistorialAsync(_equipo.Id, Arg.Any<CancellationToken>())
            .Returns([new CambioEstadoLeido(baja, "OPERATIVO", "Operativo", "BAJA", "Baja", "Ana Gómez")]);

        var historial = await _caso.HistorialAsync(_equipo.Id, CancellationToken.None);

        var fila = Assert.Single(historial);
        Assert.Equal(
            new CambioEstadoDto(baja.Id, Ahora, "OPERATIVO", "Operativo", "BAJA", "Baja", "Obsoleto", "Ana Gómez"),
            fila);
    }
}
