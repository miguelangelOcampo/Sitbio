using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Analitica;

/// <summary>Variables de un grupo o de un equipo en la agrupación K-Means (las mismas del lote de Python).</summary>
public sealed record CaracteristicasDto(double? Intervenciones, double? ProporcionCorrectivos, double? CostoConfirmado, double? TiempoMedioMin);

/// <summary>Resumen de un grupo: cuántos equipos y el promedio de sus variables.</summary>
public sealed record GrupoDto(int Grupo, int Equipos, CaracteristicasDto Promedio);

/// <summary>Un punto del gráfico de dispersión: el equipo, su grupo y sus variables.</summary>
public sealed record EquipoAgrupadoDto(Guid EquipoId, string? PlacaInventario, int Grupo, CaracteristicasDto Caracteristicas);

/// <summary>
/// Respuesta de GET /analitica/agrupamientos (API-ANA, HU-009): el último resultado del lote (ADR-006), con su
/// fecha; no es en tiempo real.
/// </summary>
public sealed record AgrupamientoDto(
    DateTimeOffset GeneradoEn,
    DateTimeOffset FechaCorte,
    int IntervencionesAnalizadas,
    int K,
    IReadOnlyList<GrupoDto> Grupos,
    IReadOnlyList<EquipoAgrupadoDto> Equipos);

/// <summary>Resultado guardado por el lote en <c>resultados_analitica</c>, sin las placas.</summary>
public sealed record ResultadoAnalitica(
    DateTimeOffset GeneradoEn,
    DateTimeOffset FechaCorte,
    int IntervencionesAnalizadas,
    int K,
    IReadOnlyList<GrupoDto> Grupos,
    IReadOnlyList<(Guid EquipoId, int Grupo, CaracteristicasDto Caracteristicas)> Equipos);

/// <summary>Resultados de la analítica en MongoDB; el tenant lo pone la persistencia (RN-008).</summary>
public interface IRepositorioAnalitica
{
    /// <summary>null si el lote aún no ha generado resultados para la institución.</summary>
    Task<ResultadoAnalitica?> UltimoAsync(CancellationToken cancellationToken);
}

/// <summary>Placas de los equipos de la institución, para rotular los puntos.</summary>
public interface IPlacasEquipos
{
    Task<IReadOnlyDictionary<Guid, string>> PlacasAsync(IReadOnlyCollection<Guid> equipos, CancellationToken cancellationToken);
}

/// <summary>
/// Agrupamiento K-Means de los equipos (HU-009, RF-028; CA-HU-009-01): lee el último resultado del lote de
/// Python y le agrega las placas. Lo consultan AC-02 y AC-05 a AC-07; el permiso lo exige la API.
/// </summary>
public sealed class ConsultaAnalitica(IRepositorioAnalitica repositorio, IPlacasEquipos placas)
{
    public const string CodigoSinResultados = "ANA-001";

    public async Task<AgrupamientoDto> AgrupamientosAsync(CancellationToken cancellationToken)
    {
        var resultado = await repositorio.UltimoAsync(cancellationToken)
            ?? throw new ErrorConflicto(CodigoSinResultados, "Aún no hay resultados de analítica para tu institución. Ejecuta el lote de analítica (python -m sitbio_analytics) y vuelve a consultar.");
        var nombres = await placas.PlacasAsync(resultado.Equipos.Select(e => e.EquipoId).ToList(), cancellationToken);
        return new AgrupamientoDto(
            resultado.GeneradoEn,
            resultado.FechaCorte,
            resultado.IntervencionesAnalizadas,
            resultado.K,
            resultado.Grupos,
            resultado.Equipos.Select(e => new EquipoAgrupadoDto(e.EquipoId, nombres.GetValueOrDefault(e.EquipoId), e.Grupo, e.Caracteristicas)).ToList());
    }
}
