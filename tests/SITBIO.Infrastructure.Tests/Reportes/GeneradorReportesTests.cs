using System.Diagnostics;
using ClosedXML.Excel;
using Sitbio.Application.Equipos;
using Sitbio.Application.Indicadores;
using Sitbio.Application.Reportes;
using Sitbio.Infrastructure.Reportes;

namespace Sitbio.Infrastructure.Tests.Reportes;

/// <summary>
/// Archivos reales con QuestPDF y ClosedXML (ADR-009): PDF y Excel del historial con las mismas columnas
/// (CA-HU-007-01/02), la hoja de vida en PDF (CA-HU-027-01) y menos de 10 s con 1 000 registros (RNF-007).
/// </summary>
public class GeneradorReportesTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 2, 13, 0, 0, TimeSpan.Zero);
    private static readonly EncabezadoReporte Encabezado = new("Clínica Demo", "900123456-7", "Historial de órdenes de trabajo", "Del 01/11/2026 al 30/11/2026", Inicio);

    private readonly GeneradorReportes _generador = new();

    private static List<FilaHistorialOrden> Filas(int cuantas) =>
        Enumerable.Range(0, cuantas).Select(i => new FilaHistorialOrden(
            Inicio.AddMinutes(i),
            $"MON-{i:0000}",
            "Monitor de signos vitales",
            "UCI adultos",
            i % 2 == 0 ? "Preventivo" : "Correctivo",
            i % 2 == 0 ? null : $"TIQ-{i:0000}",
            "Ana Mora",
            "aprobada",
            Inicio.AddMinutes(i + 90),
            "Laura Gómez")).ToList();

    /// <summary>
    /// RNF-007 mide la generación, no el arranque en frío de la biblioteca (JIT y fuentes), que ocurre una sola vez
    /// por proceso y en la CI coincide con los demás proyectos de pruebas en paralelo.
    /// </summary>
    private void Calentar(bool excel)
    {
        if (excel)
        {
            _generador.HistorialExcel(Encabezado, Filas(5));
        }
        else
        {
            _generador.HistorialPdf(Encabezado, Filas(5));
        }
    }

    [Fact]
    public void HistorialPdf_ConMilRegistros_SeGeneraEnMenosDeDiezSegundos()
    {
        Calentar(excel: false);
        var reloj = Stopwatch.StartNew();

        var pdf = _generador.HistorialPdf(Encabezado, Filas(1000));

        reloj.Stop();
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {reloj.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void HistorialExcel_TieneLasMismasColumnasYUnaFilaPorOrden()
    {
        Calentar(excel: true);
        var reloj = Stopwatch.StartNew();
        var xlsx = _generador.HistorialExcel(Encabezado, Filas(1000));
        reloj.Stop();

        using var libro = new XLWorkbook(new MemoryStream(xlsx));
        var hoja = libro.Worksheet("Historial");
        Assert.Equal(ColumnasHistorial.Nombres, Enumerable.Range(1, ColumnasHistorial.Nombres.Count).Select(c => hoja.Cell(5, c).GetString()));
        Assert.Equal(1000, hoja.LastRowUsed()!.RowNumber() - 5);
        Assert.Equal(("MON-0001", "TIQ-0001", "Aprobada", "Laura Gómez"), (hoja.Cell(7, 2).GetString(), hoja.Cell(7, 6).GetString(), hoja.Cell(7, 8).GetString(), hoja.Cell(7, 10).GetString()));
        Assert.Equal(new DateTime(2026, 11, 2, 8, 1, 0), hoja.Cell(7, 1).GetDateTime());
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {reloj.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public void HistorialSinOrdenes_GeneraLosDosArchivos()
    {
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(_generador.HistorialPdf(Encabezado, []), 0, 4));
        using var libro = new XLWorkbook(new MemoryStream(_generador.HistorialExcel(Encabezado, [])));
        Assert.Equal("Placa", libro.Worksheet("Historial").Cell(5, 2).GetString());
    }

    [Fact]
    public void HojaVida_ConMuchasIntervenciones_TieneDatosTecnicosEHistorial_EnMenosDeDiezSegundos()
    {
        var equipo = new EquipoDto(
            Guid.NewGuid(), "MON-07", "SN-1", "Mindray", "BeneView T5", Guid.NewGuid(), "Monitor de signos vitales", Guid.NewGuid(), "UCI adultos",
            null, null, "operativo", "Operativo", null, null, "registro_sanitario", "INVIMA 2021DM-1", new DateOnly(2028, 1, 1), "vigente", "IIb", true, 12, false, null);
        var eventos = Enumerable.Range(0, 1000).Select(i => new EventoLineaTiempoDto(
            Inicio.AddHours(i), TiposEvento.Intervencion, "Intervención registrada · sale «Operativo»", "Filtros sucios — limpieza", "Ana Mora", Guid.NewGuid())).ToList();
        var documento = new HojaVidaDocumento(Encabezado with { Titulo = "Hoja de vida · MON-07" }, equipo, new LineaTiempoDto(equipo.Id, "MON-07", eventos, false));

        Calentar(excel: false);
        var reloj = Stopwatch.StartNew();
        var pdf = _generador.HojaVidaPdf(documento);
        reloj.Stop();

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {reloj.Elapsed.TotalSeconds:0.0} s");
        var datos = GeneradorReportes.DatosTecnicos(documento);
        Assert.Contains(("Registro INVIMA", (string?)"INVIMA 2021DM-1 · vence el 01/01/2028"), datos);
        Assert.Contains(("Requiere calibración", (string?)"Sí · cada 12 meses"), datos);
    }

    /// <summary>PNG de 1 × 1 px.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static HojaVidaDocumento HojaVida(byte[]? foto)
    {
        var equipo = new EquipoDto(
            Guid.NewGuid(), "MON-07", "SN-1", "Mindray", "BeneView T5", Guid.NewGuid(), "Monitor de signos vitales", Guid.NewGuid(), "UCI adultos",
            null, null, "operativo", "Operativo", null, null, "registro_sanitario", "INVIMA 2021DM-1", new DateOnly(2028, 1, 1), "vigente", "IIb", true, 12, foto is not null, null);
        return new HojaVidaDocumento(Encabezado with { Titulo = "Hoja de vida · MON-07" }, equipo, new LineaTiempoDto(equipo.Id, "MON-07", [], true), foto);
    }

    private static bool TieneImagen(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf).Contains("/Subtype /Image");

    [Fact]
    public void HojaVida_ConFotografia_IncluyeLaImagenDelEquipo()
    {
        Assert.True(TieneImagen(_generador.HojaVidaPdf(HojaVida(Png))));
        Assert.False(TieneImagen(_generador.HojaVidaPdf(HojaVida(null))));
    }

    [Fact]
    public void HojaVida_ConFotografiaIlegible_SaleSinImagen()
    {
        Assert.Null(GeneradorReportes.Imagen([1, 2, 3, 4]));

        var pdf = _generador.HojaVidaPdf(HojaVida([1, 2, 3, 4]));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.False(TieneImagen(pdf));
    }

    // ---- J-03: Reportes e indicadores y Analítica con sus gráficos ----
    private static IndicadoresDocumento Indicadores(IReadOnlyList<ImagenGrafico> graficos) => new(
        Encabezado with { Titulo = "Reportes e indicadores" },
        new IndicadoresDto(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 20),
            new CumplimientoPreventivoDto(4, 3, 75),
            5.5,
            120,
            new CostoPorEquipoDto(1_500_000, 10, 150_000),
            new FueraDeServicioDto(1, 10, 10),
            CostosCompletos: false),
        [new MesIndicadoresDto(new DateOnly(2026, 10, 1), 3, 1, 75, 4.5), new MesIndicadoresDto(new DateOnly(2026, 11, 1), 2, 2, null, null)],
        new ParqueDto(8, 1, 1, [new EquipoFueraDto(Guid.NewGuid(), "MON-07", "Monitor", "UCI")]),
        graficos,
        ExportacionGraficos.ComoLeerIndicadores);

    [Fact]
    public void IndicadoresPdf_ConGraficos_IncluyeLasImagenes()
    {
        var pdf = _generador.IndicadoresPdf(Indicadores([new ImagenGrafico("Mantenimientos por mes", Png), new ImagenGrafico("Estado del parque", Png)]));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(TieneImagen(pdf));
        Assert.False(TieneImagen(_generador.IndicadoresPdf(Indicadores([]))));
    }

    [Fact]
    public void TamanoEnPuntos_LeeLaCabeceraDelPng_A192ppp()
    {
        Assert.Equal((0.375f, 0.375f), GeneradorReportes.TamanoEnPuntos(Png));
        Assert.Equal((600f, 300f), GeneradorReportes.TamanoEnPuntos([1, 2, 3]));
    }

    [Fact]
    public void FilasIndicadores_TraenElValorYElDetalle()
    {
        var filas = GeneradorReportes.FilasIndicadores(Indicadores([]).Indicadores);

        Assert.Equal(["Cumplimiento preventivo", "75 %", "3 de 4 programadas"], filas[0]);
        Assert.Equal("5,5 h", filas[1][1]);
        Assert.Equal("1 de 10 equipos activos", filas[4][2]);
    }

    [Fact]
    public void AnaliticaPdf_ConGraficoIlegible_SaleSinImagenPeroConLasTablas()
    {
        var documento = new AnaliticaDocumento(
            Encabezado with { Titulo = "Analítica de equipos" },
            "Fallas (correctivos)",
            "Costo confirmado (M COP)",
            [new FilaGrupoPdf("Alto costo y muchas fallas", 2, "8", "$ 4.000.000", "120 min"), new FilaGrupoPdf("Estables", 5, "1", "$ 900.000", "30 min")],
            Enumerable.Range(0, 60).Select(i => new FilaEquipoPdf($"PL-{i:00}", "Monitor", "Estables", "1", "0,9")).ToList(),
            [new ImagenGrafico("Dispersión", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3])],
            ["Los grupos se ordenan por número de fallas."]);

        var pdf = _generador.AnaliticaPdf(documento);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.False(TieneImagen(pdf));
        Assert.True(TieneImagen(_generador.AnaliticaPdf(documento with { Graficos = [new ImagenGrafico("Dispersión", Png)] })));
    }
}
