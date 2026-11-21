using System.Globalization;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Application.Instituciones;

namespace Sitbio.Application.Reportes;

/// <summary>Formatos de exportación (API-REP).</summary>
public static class FormatosReporte
{
    public const string Pdf = "pdf";
    public const string Excel = "xlsx";

    public const string TipoPdf = "application/pdf";
    public const string TipoExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>Una orden del historial de mantenimientos (las mismas columnas en PDF y Excel, CA-HU-007-02).</summary>
public sealed record FilaHistorialOrden(
    DateTimeOffset FechaCreacion,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string TipoMantenimiento,
    string? CodigoTiquete,
    string Tecnico,
    string Estado,
    DateTimeOffset? FechaCierre,
    string? AprobadoPor);

/// <summary>Columnas del historial, en orden; las usan los dos formatos.</summary>
public static class ColumnasHistorial
{
    public static IReadOnlyList<string> Nombres { get; } =
        ["Fecha", "Placa", "Equipo", "Ubicación", "Tipo", "Tiquete", "Técnico", "Estado", "Cierre", "Aprobada por"];
}

/// <summary>Encabezado común de los documentos.</summary>
public sealed record EncabezadoReporte(string Institucion, string Nit, string Titulo, string Subtitulo, DateTimeOffset GeneradoEn);

/// <summary>
/// Contenido de la hoja de vida en PDF (HU-027): fotografía del equipo (versión de 512 px, si tiene), datos técnicos
/// e historial completo.
/// </summary>
public sealed record HojaVidaDocumento(EncabezadoReporte Encabezado, EquipoDto Equipo, LineaTiempoDto Historial, byte[]? Foto = null);

/// <summary>Archivo generado, listo para descargar.</summary>
public sealed record ArchivoReporte(byte[] Contenido, string TipoContenido, string NombreArchivo);

/// <summary>Historial de órdenes del tenant (RN-008 lo aplica la persistencia).</summary>
public interface IRepositorioReportes
{
    /// <summary>Órdenes creadas entre <paramref name="desde"/> y <paramref name="hasta"/>, de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<FilaHistorialOrden>> HistorialOrdenesAsync(DateTimeOffset desde, DateTimeOffset hasta, int maximo, CancellationToken cancellationToken);
}

/// <summary>Arma los archivos (ADR-009: QuestPDF y ClosedXML en el servidor).</summary>
public interface IGeneradorReportes
{
    byte[] HistorialPdf(EncabezadoReporte encabezado, IReadOnlyList<FilaHistorialOrden> filas);

    byte[] HistorialExcel(EncabezadoReporte encabezado, IReadOnlyList<FilaHistorialOrden> filas);

    byte[] HojaVidaPdf(HojaVidaDocumento documento);

    /// <summary>Reportes e indicadores con sus gráficos (J-03).</summary>
    byte[] IndicadoresPdf(IndicadoresDocumento documento);

    /// <summary>Analítica con sus gráficos (J-03).</summary>
    byte[] AnaliticaPdf(AnaliticaDocumento documento);
}

/// <summary>
/// Exportación de reportes (HU-007, HU-027; API-REP): el historial de órdenes de un rango de fechas en PDF o Excel
/// (CA-HU-007-01/02) y la hoja de vida del equipo en PDF (CA-HU-027-01). Los permisos los exige la API.
/// </summary>
public sealed class ExportacionReportes(
    IRepositorioReportes repositorio,
    IRepositorioInstitucion instituciones,
    ConsultaLineaTiempo lineaTiempo,
    GestionEquipos equipos,
    IRepositorioDocumentos documentos,
    IAlmacenDocumentos almacen,
    IGeneradorReportes generador,
    TimeProvider tiempo)
{
    /// <summary>Tope de filas por archivo; RNF-007 pide menos de 10 s con 1 000.</summary>
    public const int MaximoFilas = 5000;

    public const int MaximoDiasRango = 366;

    public async Task<ArchivoReporte> HistorialOrdenesAsync(DateOnly? desde, DateOnly? hasta, string? formato, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, string[]>();
        if (desde is null)
        {
            errores["desde"] = ["Indica la fecha inicial."];
        }

        if (hasta is null)
        {
            errores["hasta"] = ["Indica la fecha final."];
        }
        else if (desde is { } d && hasta < d)
        {
            errores["hasta"] = ["La fecha final no puede ser anterior a la inicial."];
        }
        else if (desde is { } d2 && hasta.Value.DayNumber - d2.DayNumber >= MaximoDiasRango)
        {
            errores["hasta"] = ["El rango no puede superar un año."];
        }

        var f = formato?.Trim().ToLowerInvariant();
        if (f is not (FormatosReporte.Pdf or FormatosReporte.Excel))
        {
            errores["formato"] = ["El formato debe ser «pdf» o «xlsx»."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var inicio = new DateTimeOffset(desde!.Value.ToDateTime(TimeOnly.MinValue), Comun.FechaColombia.Desfase);
        var fin = new DateTimeOffset(hasta!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), Comun.FechaColombia.Desfase);
        var filas = await repositorio.HistorialOrdenesAsync(inicio, fin, MaximoFilas, cancellationToken);
        var rango = $"Del {Fecha(desde.Value)} al {Fecha(hasta.Value)} · {filas.Count} órdenes";
        var encabezado = await EncabezadoAsync("Historial de órdenes de trabajo", rango, cancellationToken);
        var nombre = $"historial-ordenes-{desde.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{hasta.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
        return f == FormatosReporte.Pdf
            ? new ArchivoReporte(generador.HistorialPdf(encabezado, filas), FormatosReporte.TipoPdf, nombre + ".pdf")
            : new ArchivoReporte(generador.HistorialExcel(encabezado, filas), FormatosReporte.TipoExcel, nombre + ".xlsx");
    }

    public async Task<ArchivoReporte> HojaVidaAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        var equipo = await equipos.ObtenerAsync(equipoId, cancellationToken);
        var historial = await lineaTiempo.ConsultarAsync(equipoId, cancellationToken);
        var encabezado = await EncabezadoAsync(
            $"Hoja de vida · {equipo.PlacaInventario}",
            $"{equipo.TipoEquipo} · {equipo.Marca} {equipo.Modelo} · serie {equipo.NumeroSerie}",
            cancellationToken);
        var foto = equipo.TieneFoto ? await FotoAsync(equipoId, cancellationToken) : null;
        var pdf = generador.HojaVidaPdf(new HojaVidaDocumento(encabezado, equipo, historial, foto));
        return new ArchivoReporte(pdf, FormatosReporte.TipoPdf, $"hoja-de-vida-{equipo.PlacaInventario}.pdf");
    }

    /// <summary>Versión de 512 px de la fotografía; si el archivo ya no está, la hoja de vida sale sin ella.</summary>
    private async Task<byte[]?> FotoAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        if (await documentos.ObtenerFotoAsync(equipoId, cancellationToken) is not { } foto)
        {
            return null;
        }

        await using var contenido = almacen.Abrir(foto.RutaAlmacenamiento);
        if (contenido is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        await contenido.CopyToAsync(bytes, cancellationToken);
        return bytes.ToArray();
    }

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private async Task<EncabezadoReporte> EncabezadoAsync(string titulo, string subtitulo, CancellationToken cancellationToken)
    {
        var institucion = await instituciones.ObtenerAsync(cancellationToken);
        return new EncabezadoReporte(institucion?.Nombre ?? "SITBIO", institucion?.Nit ?? string.Empty, titulo, subtitulo, tiempo.GetUtcNow());
    }
}
