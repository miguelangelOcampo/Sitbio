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

/// <summary>
/// Un mes de GET /indicadores/series (I-02): preventivas cumplidas y correctivas cerradas en el mes, cumplimiento del
/// plan preventivo y MTTR del mes. El mes en curso se cuenta hasta hoy.
/// </summary>
public sealed record MesIndicadoresDto(DateOnly Mes, int Preventivos, int Correctivos, double? CumplimientoPreventivo, double? MttrHoras);

/// <summary>Equipo activo fuera de servicio, para la lista de Reportes e indicadores.</summary>
public sealed record EquipoFueraDto(Guid Id, string PlacaInventario, string TipoEquipo, string Ubicacion);

/// <summary>Estado del parque hoy (GET /indicadores/parque, I-02): equipos activos por estado y los fuera de servicio.</summary>
public sealed record ParqueDto(int Operativos, int EnMantenimiento, int FueraDeServicio, IReadOnlyList<EquipoFueraDto> EquiposFuera);

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

    /// <summary>Equipos activos (sin bajas) por estado y la lista de los fuera de servicio, por placa.</summary>
    Task<ParqueDto> ParqueAsync(CancellationToken cancellationToken);
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

    public Task<ParqueDto> ParqueAsync(CancellationToken cancellationToken) => repositorio.ParqueAsync(cancellationToken);

    public const int MaximoMeses = 12;

    /// <summary>
    /// Serie mensual para los gráficos de Reportes e indicadores: los últimos <paramref name="meses"/> meses
    /// calendario hasta el actual, del más antiguo al más reciente, con los mismos criterios del tablero.
    /// </summary>
    public async Task<IReadOnlyList<MesIndicadoresDto>> SerieAsync(int meses, CancellationToken cancellationToken)
    {
        if (meses is < 1 or > MaximoMeses)
        {
            throw ErrorValidacion.DeCampo("meses", $"El número de meses va de 1 a {MaximoMeses}.");
        }

        var hoy = FechaColombia.Hoy(tiempo);
        var actual = new DateOnly(hoy.Year, hoy.Month, 1);
        var serie = new List<MesIndicadoresDto>(meses);
        for (var i = meses - 1; i >= 0; i--)
        {
            var mes = actual.AddMonths(-i);
            var fin = i == 0 ? hoy.AddDays(1) : mes.AddMonths(1);
            var datos = await repositorio.DatosAsync(
                new DateTimeOffset(mes.ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase).ToUniversalTime(),
                new DateTimeOffset(fin.ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase).ToUniversalTime(),
                cancellationToken);
            serie.Add(new MesIndicadoresDto(
                mes,
                datos.PreventivasCumplidas,
                datos.Reparaciones.Count,
                Porcentaje(datos.PreventivasCumplidas, datos.PreventivasProgramadas),
                datos.Reparaciones.Count == 0 ? null : Math.Round(datos.Reparaciones.Average(r => r.TotalHours), 1)));
        }

        return serie;
    }

    private static double? Porcentaje(int parte, int total) => total == 0 ? null : Math.Round(100.0 * parte / total, 1);
}
