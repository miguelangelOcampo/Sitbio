using System.Text;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Infrastructure.Inventario;

namespace Sitbio.Infrastructure.Tests.Reportes;

/// <summary>HU-016: lectura y escritura de la plantilla en CSV y Excel.</summary>
public class PlanillaImportacionTests
{
    private readonly PlanillaImportacion _planilla = new();

    private IReadOnlyList<IReadOnlyList<string>> LeerCsv(string texto) =>
        _planilla.Leer(new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto)]), ImportacionEquipos.FormatoCsv);

    [Fact]
    public void Csv_PuntoYComaComillasYSaltosDeLinea()
    {
        var filas = LeerCsv("Placa;Marca;Notas\r\nP-1;\"Dräger; Inc\";\"dice \"\"hola\"\"\notra línea\"\r\nP-2;GE;\r\n");

        Assert.Equal(3, filas.Count);
        Assert.Equal(["P-1", "Dräger; Inc", "dice \"hola\"\notra línea"], filas[1]);
        Assert.Equal(["P-2", "GE", ""], filas[2]);
    }

    [Fact]
    public void Csv_ConComas_SeDetectaElSeparador() =>
        Assert.Equal(["P-1", "Mindray"], LeerCsv("Placa,Marca\nP-1,Mindray")[1]);

    [Fact]
    public void Excel_LoQueSeEscribeSeLeeIgual_YConservaLosCeros()
    {
        var hojas = new[]
        {
            new HojaPlanilla("Equipos", [["Placa", "Serie"], ["000123", "SN-1"]]),
            new HojaPlanilla("Valores permitidos", [["Clase de riesgo"], ["IIa"]]),
        };

        var leido = _planilla.Leer(new MemoryStream(_planilla.Escribir(hojas, ImportacionEquipos.FormatoExcel)), ImportacionEquipos.FormatoExcel);

        Assert.Equal(["000123", "SN-1"], leido[1]);
    }

    [Fact]
    public void ArchivoDanado_EsErrorDeValidacion() =>
        Assert.Throws<ErrorValidacion>(() => _planilla.Leer(new MemoryStream([1, 2, 3]), ImportacionEquipos.FormatoExcel));

    [Fact]
    public void PlantillaCsv_EnUtf8ConBomYPuntoYComa()
    {
        var bytes = _planilla.Escribir([new HojaPlanilla("Equipos", [["Ubicación", "Clase de riesgo"]])], ImportacionEquipos.FormatoCsv);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("Ubicación;Clase de riesgo\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }
}
