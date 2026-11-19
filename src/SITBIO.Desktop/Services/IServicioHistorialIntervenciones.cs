using System.Globalization;
using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Historial de intervenciones (HU-005): GET /intervenciones, de la más reciente a la más antigua, con los
/// totales de todo el filtro. Consultan AC-01, AC-02, AC-03, AC-05 y AC-07; la API valida el rol y el tenant.
/// </summary>
public interface IServicioHistorialIntervenciones
{
    Task<PaginaHistorial> ConsultarAsync(FiltroHistorial filtro, CancellationToken cancellationToken);
}

/// <summary>Filtros; null = sin filtro. <see cref="Desde"/> y <see cref="Hasta"/> se incluyen los dos.</summary>
public sealed record FiltroHistorial(
    string? Equipo = null,
    Guid? TipoEquipoId = null,
    Guid? TecnicoId = null,
    string? TipoMantenimiento = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Pagina = 1,
    int Tamano = 20);

public sealed record IntervencionHistorial(
    Guid Id,
    Guid OrdenId,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    Guid TecnicoId,
    string Tecnico,
    string TipoMantenimiento,
    string Diagnostico,
    decimal CostoTotal,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    bool CerradaFirmada,
    DateTimeOffset FechaIntervencion,
    string? CodigoTiquete);

public sealed record ResumenHistorial(int Total, int Correctivas, int Preventivas, decimal CostoTotal, double? TiempoPromedioMin);

public sealed record PaginaHistorial(IReadOnlyList<IntervencionHistorial> Items, int Page, int PageSize, int Total, ResumenHistorial Resumen);

public sealed class ServicioHistorialIntervencionesHttp(ClienteApi cliente) : IServicioHistorialIntervenciones
{
    public Task<PaginaHistorial> ConsultarAsync(FiltroHistorial filtro, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<PaginaHistorial>(cliente, HttpMethod.Get, Ruta(filtro), null, Mensaje, cancellationToken);

    /// <summary>Ruta con los filtros presentes; las fechas en formato ISO (aaaa-mm-dd).</summary>
    public static string Ruta(FiltroHistorial filtro)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var ruta = $"intervenciones?page={filtro.Pagina}&pageSize={filtro.Tamano}";
        if (!string.IsNullOrWhiteSpace(filtro.Equipo))
        {
            ruta += $"&equipo={Uri.EscapeDataString(filtro.Equipo.Trim())}";
        }

        if (filtro.TipoEquipoId is { } tipo)
        {
            ruta += $"&tipoEquipoId={tipo}";
        }

        if (filtro.TecnicoId is { } tecnico)
        {
            ruta += $"&tecnicoId={tecnico}";
        }

        if (!string.IsNullOrWhiteSpace(filtro.TipoMantenimiento))
        {
            ruta += $"&tipoMantenimiento={Uri.EscapeDataString(filtro.TipoMantenimiento)}";
        }

        if (filtro.Desde is { } desde)
        {
            ruta += $"&desde={desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        }

        if (filtro.Hasta is { } hasta)
        {
            ruta += $"&hasta={hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        }

        return ruta;
    }

    /// <summary>400 trae el mensaje de la API (filtro inválido); 503 = el servidor de documentos no respondió.</summary>
    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar el historial de intervenciones.",
        503 => "El servidor de documentos no respondió. Intenta de nuevo en unos minutos.",
        >= 500 => "El servidor no pudo consultar el historial. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
