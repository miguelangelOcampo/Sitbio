using System.Diagnostics;
using ClosedXML.Excel;
using Sitbio.Application.Equipos;
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
}
