using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>
/// CSV y Excel para la importación masiva (HU-016). El CSV se lee en UTF-8 (con o sin BOM) y acepta
/// punto y coma o coma como separador (se elige por el encabezado); la plantilla CSV usa punto y coma,
/// que es el separador de Excel en español. El Excel se lee y escribe con ClosedXML (ADR-009).
/// </summary>
public sealed class PlanillaImportacion : IPlanillaImportacion
{
    private const string NoSePudoLeer = "No se pudo leer el archivo; revisa que sea la plantilla en CSV o Excel (.xlsx).";

    public IReadOnlyList<IReadOnlyList<string>> Leer(Stream contenido, string formato)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        try
        {
            return formato == ImportacionEquipos.FormatoExcel ? LeerExcel(contenido) : LeerCsv(contenido);
        }
        catch (Exception error) when (error is not ErrorValidacion and not OperationCanceledException)
        {
            throw ErrorValidacion.DeCampo("archivo", NoSePudoLeer);
        }
    }

    public byte[] Escribir(IReadOnlyList<HojaPlanilla> hojas, string formato)
    {
        ArgumentNullException.ThrowIfNull(hojas);
        return formato == ImportacionEquipos.FormatoExcel ? EscribirExcel(hojas) : EscribirCsv(hojas[0]);
    }

    private static List<IReadOnlyList<string>> LeerExcel(Stream contenido)
    {
        using var libro = new XLWorkbook(contenido);
        var hoja = libro.Worksheets.First();
        var usado = hoja.RangeUsed();
        var filas = new List<IReadOnlyList<string>>();
        if (usado is null)
        {
            return filas;
        }

        var ultimaColumna = usado.LastColumn().ColumnNumber();
        for (var f = 1; f <= usado.LastRow().RowNumber(); f++)
        {
            var celdas = new List<string>(ultimaColumna);
            for (var c = 1; c <= ultimaColumna; c++)
            {
                celdas.Add(Texto(hoja.Cell(f, c)));
            }

            filas.Add(celdas);
        }

        return filas;
    }

    private static string Texto(IXLCell celda) => celda.DataType switch
    {
        XLDataType.DateTime => celda.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XLDataType.Number => celda.GetDouble().ToString(CultureInfo.InvariantCulture),
        XLDataType.Boolean => celda.GetBoolean() ? "Sí" : "No",
        _ => celda.GetFormattedString(),
    };

    private static List<IReadOnlyList<string>> LeerCsv(Stream contenido)
    {
        using var lector = new StreamReader(contenido, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        var texto = lector.ReadToEnd();
        var primeraLinea = texto.Split('\n', 2)[0];
        var separador = primeraLinea.Count(c => c == ';') >= primeraLinea.Count(c => c == ',') ? ';' : ',';

        var filas = new List<IReadOnlyList<string>>();
        var fila = new List<string>();
        var celda = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    celda.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    celda.Append(c);
                }
            }
            else if (c == '"')
            {
                entreComillas = true;
            }
            else if (c == separador)
            {
                fila.Add(celda.ToString());
                celda.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                {
                    i++;
                }

                fila.Add(celda.ToString());
                celda.Clear();
                filas.Add(fila);
                fila = [];
            }
            else
            {
                celda.Append(c);
            }
        }

        if (celda.Length > 0 || fila.Count > 0)
        {
            fila.Add(celda.ToString());
            filas.Add(fila);
        }

        return filas;
    }

    private static byte[] EscribirCsv(HojaPlanilla hoja)
    {
        var texto = new StringBuilder();
        foreach (var fila in hoja.Filas)
        {
            texto.AppendJoin(';', fila.Select(Escapar)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())];
    }

    private static string Escapar(string valor) =>
        valor.IndexOfAny([';', ',', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : valor;

    private static byte[] EscribirExcel(IReadOnlyList<HojaPlanilla> hojas)
    {
        using var libro = new XLWorkbook();
        foreach (var hoja in hojas)
        {
            var destino = libro.Worksheets.Add(hoja.Nombre);

            // Todo como texto: placas y series como «000123» no pierden los ceros.
            destino.Columns(1, Math.Max(1, hoja.Filas.Max(f => f.Count))).Style.NumberFormat.Format = "@";
            for (var f = 0; f < hoja.Filas.Count; f++)
            {
                for (var c = 0; c < hoja.Filas[f].Count; c++)
                {
                    destino.Cell(f + 1, c + 1).SetValue(hoja.Filas[f][c]);
                }
            }

            var encabezado = destino.Range(1, 1, 1, Math.Max(1, hoja.Filas[0].Count));
            encabezado.Style.Font.Bold = true;
            encabezado.Style.Font.FontColor = XLColor.White;
            encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
            destino.SheetView.FreezeRows(1);
            destino.Columns().AdjustToContents();
        }

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }
}
