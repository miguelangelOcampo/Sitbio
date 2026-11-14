using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;

namespace Sitbio.Application.Indicadores;

/// <summary>
/// Respuesta de GET /indicadores (API-KPI, HU-026): los cinco indicadores del tenant en el periodo.
/// Un valor es null cuando no hay datos para calcularlo. <see cref="CostosCompletos"/> es false si MongoDB no
/// respondió y el costo no se pudo leer.
/// </summary>
public sealed record IndicadoresDto(
    DateOnly Desde,
    DateOnly Hasta,
    CumplimientoPreventivoDto CumplimientoPreventivo,
    double? MttrHoras,
    double? MtbfHoras,
    CostoPorEquipoDto CostoPorEquipo,
    FueraDeServicioDto FueraDeServicio,
    bool CostosCompletos);

/// <summary>Preventivas programadas en el periodo y cuántas se cerraron; porcentaje null si no hubo programadas.</summary>
public sealed record CumplimientoPreventivoDto(int Programadas, int Cumplidas, double? Porcentaje);

/// <summary>Costo de las intervenciones del periodo repartido entre los equipos activos.</summary>
public sealed record CostoPorEquipoDto(decimal? CostoTotal, int EquiposActivos, decimal? Promedio);

/// <summary>Equipos activos fuera de servicio hoy y su porcentaje sobre el inventario activo.</summary>
public sealed record FueraDeServicioDto(int Equipos, int EquiposActivos, double? Porcentaje);

/// <summary>Lo que PostgreSQL aporta al tablero, ya filtrado por tenant (RN-008).</summary>
public sealed record DatosIndicadores(
    int PreventivasProgramadas,
    int PreventivasCumplidas,
    IReadOnlyList<TimeSpan> Reparaciones,
    int Fallas,
    int EquiposActivos,
    int EquiposFueraDeServicio);

public interface IRepositorioIndicadores
{
    /// <summary>
    /// Datos del periodo [desde, hasta): preventivas con fecha programada en el periodo (cumplidas = cerradas o
    /// aprobadas), duración de las correctivas cerradas en el periodo (creación → cierre), fallas reportadas y el
    /// inventario activo (sin bajas).
    /// </summary>
    Task<DatosIndicadores> DatosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken);
}

/// <summary>
/// Tablero de indicadores (HU-026, RF-026; CA-HU-026-01): cumplimiento del plan preventivo, MTTR, MTBF, costo
/// por equipo y equipos fuera de servicio, sobre los datos de la institución del token. Solo consulta; los
/// roles (AC-01, AC-02, AC-05 a AC-07) los exige la API (CA-HU-026-02).
/// </summary>
public sealed class ConsultaIndicadores(IRepositorioIndicadores repositorio, IRepositorioIntervenciones intervenciones, TimeProvider tiempo)
{
    /// <summary>Sin periodo, los últimos 30 días hasta hoy.</summary>
    public const int DiasPorDefecto = 30;

    public const int MaximoDias = 366;

    public async Task<IndicadoresDto> ConsultarAsync(DateOnly? desde, DateOnly? hasta, CancellationToken cancellationToken)
    {
        var fin = hasta ?? FechaColombia.Hoy(tiempo);
        var inicio = desde ?? fin.AddDays(-(DiasPorDefecto - 1));
        if (fin < inicio)
        {
            throw ErrorValidacion.DeCampo("hasta", "La fecha final no puede ser anterior a la inicial.");
        }

        if (fin.DayNumber - inicio.DayNumber >= MaximoDias)
        {
            throw ErrorValidacion.DeCampo("hasta", "El periodo no puede superar un año.");
        }

        var desdeUtc = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase).ToUniversalTime();
        var hastaUtc = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase).ToUniversalTime();
        var datos = await repositorio.DatosAsync(desdeUtc, hastaUtc, cancellationToken);

        decimal? costo;
        try
        {
            costo = await intervenciones.CostoEnPeriodoAsync(desdeUtc, hastaUtc, cancellationToken);
        }
        catch (ErrorServicioNoDisponible)
        {
            costo = null;
        }

        var horasPeriodo = (hastaUtc - desdeUtc).TotalHours;
        return new IndicadoresDto(
            inicio,
            fin,
            new CumplimientoPreventivoDto(datos.PreventivasProgramadas, datos.PreventivasCumplidas, Porcentaje(datos.PreventivasCumplidas, datos.PreventivasProgramadas)),
            datos.Reparaciones.Count == 0 ? null : Math.Round(datos.Reparaciones.Average(r => r.TotalHours), 1),
            datos.Fallas == 0 || datos.EquiposActivos == 0 ? null : Math.Round(horasPeriodo * datos.EquiposActivos / datos.Fallas, 1),
            new CostoPorEquipoDto(costo, datos.EquiposActivos, costo is { } c && datos.EquiposActivos > 0 ? Math.Round(c / datos.EquiposActivos, 2) : null),
            new FueraDeServicioDto(datos.EquiposFueraDeServicio, datos.EquiposActivos, Porcentaje(datos.EquiposFueraDeServicio, datos.EquiposActivos)),
            costo is not null);
    }

    private static double? Porcentaje(int parte, int total) => total == 0 ? null : Math.Round(100.0 * parte / total, 1);
}
