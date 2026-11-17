using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Win32;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Tablero (API-KPI, HU-026) y exportaciones (API-REP, HU-007 y HU-027). El permiso y el tenant los decide la
/// API (RNF-003, RN-008).
/// </summary>
public interface IServicioReportes
{
    Task<Indicadores> IndicadoresAsync(DateOnly? desde, DateOnly? hasta, CancellationToken cancellationToken);

    /// <summary>Historial de órdenes del rango; <paramref name="formato"/> es «pdf» o «xlsx».</summary>
    Task<ArchivoDescargado> HistorialOrdenesAsync(DateOnly desde, DateOnly hasta, string formato, CancellationToken cancellationToken);

    Task<ArchivoDescargado> HojaVidaAsync(Guid equipoId, CancellationToken cancellationToken);
}

/// <summary>Respuesta de GET /indicadores (IndicadoresDto).</summary>
public sealed record Indicadores(
    DateOnly Desde,
    DateOnly Hasta,
    CumplimientoPreventivo CumplimientoPreventivo,
    double? MttrHoras,
    double? MtbfHoras,
    CostoPorEquipo CostoPorEquipo,
    FueraDeServicio FueraDeServicio,
    bool CostosCompletos);

public sealed record CumplimientoPreventivo(int Programadas, int Cumplidas, double? Porcentaje);

public sealed record CostoPorEquipo(decimal? CostoTotal, int EquiposActivos, decimal? Promedio);

public sealed record FueraDeServicio(int Equipos, int EquiposActivos, double? Porcentaje);

/// <summary>Archivo que respondió la API, con el nombre que propone.</summary>
public sealed record ArchivoDescargado(byte[] Contenido, string NombreArchivo);

public sealed class ServicioReportesHttp(ClienteApi cliente) : IServicioReportes
{
    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar o exportar estos reportes.",
        404 => "El equipo no existe o no pertenece a tu institución.",
        _ => null,
    };

    public Task<Indicadores> IndicadoresAsync(DateOnly? desde, DateOnly? hasta, CancellationToken cancellationToken)
    {
        var partes = new List<string>();
        if (desde is { } d)
        {
            partes.Add($"desde={Iso(d)}");
        }

        if (hasta is { } h)
        {
            partes.Add($"hasta={Iso(h)}");
        }

        var ruta = partes.Count == 0 ? "indicadores" : $"indicadores?{string.Join('&', partes)}";
        return PeticionApi.EnviarAsync<Indicadores>(cliente, HttpMethod.Get, ruta, null, Mensaje, cancellationToken);
    }

    public Task<ArchivoDescargado> HistorialOrdenesAsync(DateOnly desde, DateOnly hasta, string formato, CancellationToken cancellationToken) =>
        PeticionApi.DescargarAsync(cliente, $"reportes/ordenes?desde={Iso(desde)}&hasta={Iso(hasta)}&formato={formato}", Mensaje, cancellationToken);

    public Task<ArchivoDescargado> HojaVidaAsync(Guid equipoId, CancellationToken cancellationToken) =>
        PeticionApi.DescargarAsync(cliente, $"equipos/{equipoId}/hoja-vida.pdf", Mensaje, cancellationToken);

    private static string Iso(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Elegir dónde guardar un archivo y escribirlo; abstraído para probar los ViewModels.</summary>
public interface IDialogoArchivos
{
    /// <summary>Ruta elegida o null si el usuario canceló.</summary>
    string? ElegirDestino(string nombreSugerido);

    void Guardar(string ruta, byte[] contenido);

    /// <summary>Archivo elegido para abrir (HU-015: PDF, JPG o PNG) o null si el usuario canceló.</summary>
    string? ElegirArchivo(string titulo, string filtro);

    /// <summary>Tamaño en bytes del archivo elegido.</summary>
    long Tamano(string ruta);

    byte[] Leer(string ruta);
}

public sealed class DialogoArchivosWpf : IDialogoArchivos
{
    public string? ElegirDestino(string nombreSugerido)
    {
        var extension = Path.GetExtension(nombreSugerido);
        var dialogo = new SaveFileDialog
        {
            FileName = nombreSugerido,
            DefaultExt = extension,
            Filter = extension == ".xlsx" ? "Libro de Excel (*.xlsx)|*.xlsx" : "Documento PDF (*.pdf)|*.pdf",
            AddExtension = true,
        };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    }

    public void Guardar(string ruta, byte[] contenido) => File.WriteAllBytes(ruta, contenido);

    public string? ElegirArchivo(string titulo, string filtro)
    {
        var dialogo = new OpenFileDialog { Title = titulo, Filter = filtro, CheckFileExists = true, Multiselect = false };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    }

    public long Tamano(string ruta) => new FileInfo(ruta).Length;

    public byte[] Leer(string ruta) => File.ReadAllBytes(ruta);
}
