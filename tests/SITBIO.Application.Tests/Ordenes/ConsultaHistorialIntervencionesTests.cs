using NSubstitute;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;

namespace Sitbio.Application.Tests.Ordenes;

/// <summary>
/// Historial de intervenciones (HU-005): validación de los filtros, fechas de Colombia con el día final incluido,
/// paginación, filas con los datos de PostgreSQL y consulta vacía sin tocar MongoDB cuando ningún equipo cumple.
/// </summary>
public class ConsultaHistorialIntervencionesTests
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioHistorialIntervenciones _datos = Substitute.For<IRepositorioHistorialIntervenciones>();

    public ConsultaHistorialIntervencionesTests()
    {
        _intervenciones.ConsultarAsync(default!, default, default, default).ReturnsForAnyArgs(new ResultadoIntervenciones([], 0, 0, 0, null));
        _datos.DatosEquiposAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, EquipoDelHistorial>());
        _datos.NombresAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string>());
        _datos.TiquetesDeOrdenesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string>());
    }

    private ConsultaHistorialIntervenciones Caso() => new(_intervenciones, _datos);

    private static Intervencion Intervencion(Guid equipo, Guid tecnico, Guid orden, decimal costo) => new(
        Guid.NewGuid(), orden, equipo, tecnico, "correctivo", "Fuente averiada", "Se reemplazó", [], 20000m, costo, 45, "Operativo",
        new Dictionary<string, object>(), null, new DateTimeOffset(2026, 11, 18, 10, 0, 0, Colombia), true);

    [Theory]
    [InlineData(0, 20, null, "page")]
    [InlineData(1, 0, null, "pageSize")]
    [InlineData(1, 101, null, "pageSize")]
    [InlineData(1, 20, "calibracion", "tipoMantenimiento")]
    public async Task FiltrosInvalidos_ResponderConElCampo(int pagina, int tamano, string? tipo, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            Caso().ConsultarAsync(new FiltroHistorialIntervenciones(TipoMantenimiento: tipo, Pagina: pagina, Tamano: tamano), CancellationToken.None));

        Assert.Contains(campo, error.Errores.Keys);
        await _intervenciones.DidNotReceiveWithAnyArgs().ConsultarAsync(default!, default, default, default);
    }

    [Fact]
    public async Task FechaFinalAnteriorALaInicial_Rechaza()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().ConsultarAsync(
            new FiltroHistorialIntervenciones(Desde: new DateOnly(2026, 11, 10), Hasta: new DateOnly(2026, 11, 1)), CancellationToken.None));

        Assert.Contains("hasta", error.Errores.Keys);
    }

    [Fact]
    public async Task Criterios_FechasDeColombiaConElDiaFinalIncluido_TipoNormalizado_YDesplazamientoDeLaPagina()
    {
        var tecnico = Guid.NewGuid();

        await Caso().ConsultarAsync(
            new FiltroHistorialIntervenciones(
                TecnicoId: tecnico, TipoMantenimiento: " Preventivo ", Desde: new DateOnly(2026, 11, 1), Hasta: new DateOnly(2026, 11, 30), Pagina: 3, Tamano: 10),
            CancellationToken.None);

        await _intervenciones.Received(1).ConsultarAsync(
            Arg.Is<CriteriosIntervenciones>(c =>
                c.EquipoIds == null
                && c.TecnicoId == tecnico
                && c.TipoMantenimiento == "preventivo"
                && c.Desde == new DateTimeOffset(2026, 11, 1, 0, 0, 0, Colombia)
                && c.Hasta == new DateTimeOffset(2026, 12, 1, 0, 0, 0, Colombia)),
            20,
            10,
            Arg.Any<CancellationToken>());
        await _datos.DidNotReceiveWithAnyArgs().EquiposAsync(default, default, default);
    }

    [Fact]
    public async Task NingunEquipoCumpleElFiltro_DevuelvePaginaVacia_SinConsultarMongo()
    {
        _datos.EquiposAsync("BOM", null, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var pagina = await Caso().ConsultarAsync(new FiltroHistorialIntervenciones(Equipo: "  BOM "), CancellationToken.None);

        Assert.Empty(pagina.Items);
        Assert.Equal((0, 0), (pagina.Total, pagina.Resumen.Total));
        await _intervenciones.DidNotReceiveWithAnyArgs().ConsultarAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Filas_LlevanPlacaTipoTecnicoYTiquete_YElResumenEsDeTodoElFiltro()
    {
        var tipo = Guid.NewGuid();
        var (bomba, monitor) = (Guid.NewGuid(), Guid.NewGuid());
        var tecnico = Guid.NewGuid();
        var (ordenCorrectiva, ordenPreventiva) = (Guid.NewGuid(), Guid.NewGuid());
        _datos.EquiposAsync(null, tipo, Arg.Any<CancellationToken>()).Returns(new List<Guid> { bomba, monitor });
        _intervenciones.ConsultarAsync(default!, default, default, default).ReturnsForAnyArgs(new ResultadoIntervenciones(
            [Intervencion(bomba, tecnico, ordenCorrectiva, 50000m), Intervencion(monitor, Guid.NewGuid(), ordenPreventiva, 30000m)], 7, 4, 410000m, 52.5));
        _datos.DatosEquiposAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, EquipoDelHistorial>
        {
            [bomba] = new("BOM-05", "Bomba de infusión"),
        });
        _datos.NombresAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string> { [tecnico] = "Carlos Pérez" });
        _datos.TiquetesDeOrdenesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string> { [ordenCorrectiva] = "TIQ-000031" });

        var pagina = await Caso().ConsultarAsync(new FiltroHistorialIntervenciones(TipoEquipoId: tipo, Tamano: 2), CancellationToken.None);

        await _intervenciones.Received(1).ConsultarAsync(
            Arg.Is<CriteriosIntervenciones>(c => c.EquipoIds!.SequenceEqual(new[] { bomba, monitor })), 0, 2, Arg.Any<CancellationToken>());
        var (primera, segunda) = (pagina.Items[0], pagina.Items[1]);
        Assert.Equal(("BOM-05", "Bomba de infusión", "Carlos Pérez", "TIQ-000031"), (primera.PlacaInventario, primera.TipoEquipo, primera.Tecnico, primera.CodigoTiquete));
        Assert.Equal(50000m, primera.CostoTotal);

        // Sin datos en PostgreSQL (equipo o técnico ya no visibles), la fila sale con textos vacíos y sin tiquete.
        Assert.Equal((string.Empty, string.Empty, (string?)null), (segunda.PlacaInventario, segunda.Tecnico, segunda.CodigoTiquete));

        Assert.Equal(7, pagina.Total);
        Assert.Equal(new ResumenHistorial(7, 4, 3, 410000m, 52.5), pagina.Resumen);
    }
}
