using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Resultados K-Means del lote de analítica (API-ANA, HU-009); permiso y tenant los decide la API.</summary>
public interface IServicioAnalitica
{
    Task<Agrupamiento> AgrupamientosAsync(CancellationToken cancellationToken);
}

public sealed record Caracteristicas(double? Intervenciones, double? ProporcionCorrectivos, double? CostoConfirmado, double? TiempoMedioMin);

public sealed record GrupoAnalitica(int Grupo, int Equipos, Caracteristicas Promedio);

public sealed record EquipoAgrupado(Guid EquipoId, string? PlacaInventario, int Grupo, Caracteristicas Caracteristicas);

/// <summary>Respuesta de GET /analitica/agrupamientos (AgrupamientoDto).</summary>
public sealed record Agrupamiento(
    DateTimeOffset GeneradoEn,
    DateTimeOffset FechaCorte,
    int IntervencionesAnalizadas,
    int K,
    IReadOnlyList<GrupoAnalitica> Grupos,
    IReadOnlyList<EquipoAgrupado> Equipos);

public sealed class ServicioAnaliticaHttp(ClienteApi cliente) : IServicioAnalitica
{
    public Task<Agrupamiento> AgrupamientosAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Agrupamiento>(cliente, HttpMethod.Get, "analitica/agrupamientos", null, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado == 403 ? "Tu rol no permite consultar la analítica." : null;
}
