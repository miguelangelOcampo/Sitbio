using NSubstitute;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Reportes;

namespace Sitbio.Application.Tests.Reportes;

/// <summary>Exportación del historial de órdenes (HU-007): rango y formato válidos, y el archivo del formato pedido.</summary>
public class ExportacionReportesTests
{
    private readonly IRepositorioReportes _repositorio = Substitute.For<IRepositorioReportes>();
    private readonly IGeneradorReportes _generador = Substitute.For<IGeneradorReportes>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();

    private ExportacionReportes Caso() =>
        new(_repositorio, _instituciones, null!, null!, null!, null!, _generador, TimeProvider.System);

    [Theory]
    [InlineData(null, "2026-11-30", "pdf", "desde")]
    [InlineData("2026-11-01", null, "pdf", "hasta")]
    [InlineData("2026-11-30", "2026-11-01", "pdf", "hasta")]
    [InlineData("2025-01-01", "2026-11-30", "pdf", "hasta")]
    [InlineData("2026-11-01", "2026-11-30", "docx", "formato")]
    [InlineData("2026-11-01", "2026-11-30", null, "formato")]
    public async Task DatosInvalidos_ResponderConElCampo(string? desde, string? hasta, string? formato, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().HistorialOrdenesAsync(
            desde is null ? null : DateOnly.Parse(desde), hasta is null ? null : DateOnly.Parse(hasta), formato, CancellationToken.None));

        Assert.Contains(campo, error.Errores.Keys);
        await _repositorio.DidNotReceiveWithAnyArgs().HistorialOrdenesAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("pdf", FormatosReporte.TipoPdf, "historial-ordenes-20261101-20261130.pdf")]
    [InlineData("XLSX", FormatosReporte.TipoExcel, "historial-ordenes-20261101-20261130.xlsx")]
    public async Task Formato_GeneraElArchivoPedido_ConElDiaFinalIncluido(string formato, string tipo, string nombre)
    {
        _generador.HistorialPdf(default!, default!).ReturnsForAnyArgs([1]);
        _generador.HistorialExcel(default!, default!).ReturnsForAnyArgs([2]);

        var archivo = await Caso().HistorialOrdenesAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), formato, CancellationToken.None);

        Assert.Equal((tipo, nombre), (archivo.TipoContenido, archivo.NombreArchivo));
        var desfase = TimeSpan.FromHours(-5);
        await _repositorio.Received(1).HistorialOrdenesAsync(
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, desfase),
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, desfase),
            ExportacionReportes.MaximoFilas,
            Arg.Any<CancellationToken>());
    }
}
