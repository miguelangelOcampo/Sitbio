using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Indicadores;
using Sitbio.Application.Ordenes;

namespace Sitbio.Application.Tests.Indicadores;

/// <summary>Los cinco indicadores del tablero (HU-026, CA-HU-026-01) con el periodo en fechas de Colombia.</summary>
public class ConsultaIndicadoresTests
{
    private static readonly DateOnly Desde = new(2026, 11, 1);
    private static readonly DateOnly Hasta = new(2026, 11, 30);
    private readonly IRepositorioIndicadores _repositorio = Substitute.For<IRepositorioIndicadores>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();

    private ConsultaIndicadores Caso() => new(_repositorio, _intervenciones, TimeProvider.System);

    [Fact]
    public async Task Calcula_LosCincoIndicadores()
    {
        _repositorio.DatosAsync(default, default, default).ReturnsForAnyArgs(
            new DatosIndicadores(8, 6, [TimeSpan.FromHours(4), TimeSpan.FromHours(8)], 3, 20, 2));
        _intervenciones.CostoEnPeriodoAsync(default, default, default).ReturnsForAnyArgs(500000m);

        var kpi = await Caso().ConsultarAsync(Desde, Hasta, CancellationToken.None);

        Assert.Equal(new CumplimientoPreventivoDto(8, 6, 75.0), kpi.CumplimientoPreventivo);
        Assert.Equal(6.0, kpi.MttrHoras);
        Assert.Equal(Math.Round(30 * 24 * 20 / 3.0, 1), kpi.MtbfHoras);
        Assert.Equal(new CostoPorEquipoDto(500000m, 20, 25000m), kpi.CostoPorEquipo);
        Assert.Equal(new FueraDeServicioDto(2, 20, 10.0), kpi.FueraDeServicio);
        Assert.True(kpi.CostosCompletos);
        var desfase = TimeSpan.FromHours(-5);
        await _repositorio.Received(1).DatosAsync(
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, desfase), new DateTimeOffset(2026, 12, 1, 0, 0, 0, desfase), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinDatos_LosIndicadoresQuedanVacios_SinDividirPorCero()
    {
        _repositorio.DatosAsync(default, default, default).ReturnsForAnyArgs(new DatosIndicadores(0, 0, [], 0, 0, 0));

        var kpi = await Caso().ConsultarAsync(Desde, Hasta, CancellationToken.None);

        Assert.Equal((null, null, null, null, null), (kpi.CumplimientoPreventivo.Porcentaje, kpi.MttrHoras, kpi.MtbfHoras, kpi.CostoPorEquipo.Promedio, kpi.FueraDeServicio.Porcentaje));
    }

    [Fact]
    public async Task SinMongo_ElCostoQuedaVacio_YSeAvisa()
    {
        _repositorio.DatosAsync(default, default, default).ReturnsForAnyArgs(new DatosIndicadores(1, 1, [], 0, 5, 0));
        _intervenciones.CostoEnPeriodoAsync(default, default, default).ThrowsAsyncForAnyArgs(new ErrorServicioNoDisponible(new TimeoutException()));

        var kpi = await Caso().ConsultarAsync(Desde, Hasta, CancellationToken.None);

        Assert.False(kpi.CostosCompletos);
        Assert.Equal(new CostoPorEquipoDto(null, 5, null), kpi.CostoPorEquipo);
        Assert.Equal(100.0, kpi.CumplimientoPreventivo.Porcentaje);
    }

    [Fact]
    public async Task SinPeriodo_UsaLosUltimos30Dias()
    {
        _repositorio.DatosAsync(default, default, default).ReturnsForAnyArgs(new DatosIndicadores(0, 0, [], 0, 0, 0));

        var kpi = await Caso().ConsultarAsync(null, null, CancellationToken.None);

        Assert.Equal(ConsultaIndicadores.DiasPorDefecto - 1, kpi.Hasta.DayNumber - kpi.Desde.DayNumber);
    }

    [Theory]
    [InlineData("2026-11-30", "2026-11-01")]
    [InlineData("2025-01-01", "2026-11-30")]
    public async Task PeriodoInvalido_Responde400(string desde, string hasta) =>
        Assert.Contains("hasta", (await Assert.ThrowsAsync<ErrorValidacion>(() =>
            Caso().ConsultarAsync(DateOnly.Parse(desde), DateOnly.Parse(hasta), CancellationToken.None))).Errores.Keys);

    /// <summary>I-02: serie mensual para los gráficos de Reportes e indicadores.</summary>
    [Fact]
    public async Task Serie_UnMesPorConsulta_DelMasAntiguoAlActual_ElActualHastaHoy()
    {
        var hoy = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(-5));
        _repositorio.DatosAsync(default, default, default).ReturnsForAnyArgs(
            new DatosIndicadores(4, 3, [TimeSpan.FromHours(2), TimeSpan.FromHours(5)], 1, 10, 0),
            new DatosIndicadores(0, 0, [], 0, 10, 0),
            new DatosIndicadores(2, 2, [TimeSpan.FromHours(1)], 1, 10, 1));

        var serie = await new ConsultaIndicadores(_repositorio, _intervenciones, new Reloj(hoy)).SerieAsync(3, CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)], serie.Select(m => m.Mes));
        Assert.Equal(new MesIndicadoresDto(new DateOnly(2026, 9, 1), 3, 2, 75.0, 3.5), serie[0]);
        Assert.Equal(new MesIndicadoresDto(new DateOnly(2026, 10, 1), 0, 0, null, null), serie[1]);
        var desfase = TimeSpan.FromHours(-5);
        await _repositorio.Received(1).DatosAsync(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, desfase), new DateTimeOffset(2026, 10, 1, 0, 0, 0, desfase), Arg.Any<CancellationToken>());
        await _repositorio.Received(1).DatosAsync(
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, desfase), new DateTimeOffset(2026, 11, 20, 0, 0, 0, desfase), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task Serie_FueraDeUnoADoceMeses_EsUnErrorDeValidacion(int meses) =>
        await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().SerieAsync(meses, CancellationToken.None));

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora.ToUniversalTime();
    }
}
