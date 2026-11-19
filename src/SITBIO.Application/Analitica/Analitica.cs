using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Analitica;

/// <summary>Variables de un grupo o de un equipo en la agrupación K-Means (las mismas del lote de Python).</summary>
public sealed record CaracteristicasDto(double? Intervenciones, double? ProporcionCorrectivos, double? CostoConfirmado, double? TiempoMedioMin);

/// <summary>Resumen de un grupo: cuántos equipos, el promedio de sus variables y su nombre (I-03).</summary>
public sealed record GrupoDto(int Grupo, int Equipos, CaracteristicasDto Promedio, string? Nombre = null);

/// <summary>Un punto del gráfico de dispersión: el equipo, su grupo y sus variables.</summary>
public sealed record EquipoAgrupadoDto(Guid EquipoId, string? PlacaInventario, int Grupo, CaracteristicasDto Caracteristicas, string? TipoEquipo = null);

/// <summary>Agrupación con un número de grupos (k de 2 a 6) y su inercia; el cliente cambia de k sin recalcular.</summary>
public sealed record AgrupacionDto(int K, double? Inercia, IReadOnlyList<GrupoDto> Grupos, IReadOnlyList<EquipoAgrupadoDto> Equipos);

/// <summary>Placa y tipo de un equipo, para rotular los puntos.</summary>
public sealed record DatosEquipo(string Placa, string Tipo);

/// <summary>Agrupación guardada por el lote, sin las placas.</summary>
public sealed record AgrupacionGuardada(
    int K,
    double? Inercia,
    IReadOnlyList<GrupoDto> Grupos,
    IReadOnlyList<(Guid EquipoId, int Grupo, CaracteristicasDto Caracteristicas)> Equipos);

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
    IReadOnlyList<EquipoAgrupadoDto> Equipos,
    IReadOnlyList<AgrupacionDto> Agrupaciones);

/// <summary>Resultado guardado por el lote en <c>resultados_analitica</c>, sin las placas.</summary>
public sealed record ResultadoAnalitica(
    DateTimeOffset GeneradoEn,
    DateTimeOffset FechaCorte,
    int IntervencionesAnalizadas,
    int K,
    IReadOnlyList<GrupoDto> Grupos,
    IReadOnlyList<(Guid EquipoId, int Grupo, CaracteristicasDto Caracteristicas)> Equipos,
    IReadOnlyList<AgrupacionGuardada>? Agrupaciones = null);

/// <summary>Resultados de la analítica en MongoDB; el tenant lo pone la persistencia (RN-008).</summary>
public interface IRepositorioAnalitica
{
    /// <summary>null si el lote aún no ha generado resultados para la institución.</summary>
    Task<ResultadoAnalitica?> UltimoAsync(CancellationToken cancellationToken);
}

/// <summary>Placa y tipo de los equipos de la institución, para rotular los puntos.</summary>
public interface IPlacasEquipos
{
    Task<IReadOnlyDictionary<Guid, DatosEquipo>> PlacasAsync(IReadOnlyCollection<Guid> equipos, CancellationToken cancellationToken);
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
            ?? throw new ErrorConflicto(CodigoSinResultados, "Aún no hay resultados de analítica para tu institución. Un ingeniero biomédico puede calcularlos con «Recalcular»; después vuelve a consultar.");
        var datos = await placas.PlacasAsync(resultado.Equipos.Select(e => e.EquipoId).ToList(), cancellationToken);
        IReadOnlyList<EquipoAgrupadoDto> Equipos(IEnumerable<(Guid EquipoId, int Grupo, CaracteristicasDto Caracteristicas)> equipos) => equipos
            .Select(e => datos.GetValueOrDefault(e.EquipoId) is { } d
                ? new EquipoAgrupadoDto(e.EquipoId, d.Placa, e.Grupo, e.Caracteristicas, d.Tipo)
                : new EquipoAgrupadoDto(e.EquipoId, null, e.Grupo, e.Caracteristicas))
            .ToList();

        var principal = Equipos(resultado.Equipos);

        // Un resultado anterior a I-03 solo trae la agrupación por defecto.
        var agrupaciones = resultado.Agrupaciones is { Count: > 0 } todas
            ? todas.OrderBy(a => a.K).Select(a => new AgrupacionDto(a.K, a.Inercia, a.Grupos, Equipos(a.Equipos))).ToList()
            : [new AgrupacionDto(resultado.K, null, resultado.Grupos, principal)];
        return new AgrupamientoDto(
            resultado.GeneradoEn,
            resultado.FechaCorte,
            resultado.IntervencionesAnalizadas,
            resultado.K,
            resultado.Grupos,
            principal,
            agrupaciones);
    }
}
