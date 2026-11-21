using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Resultados K-Means del lote de analítica (API-ANA, HU-009); permiso y tenant los decide la API.</summary>
public interface IServicioAnalitica
{
    Task<Agrupamiento> AgrupamientosAsync(CancellationToken cancellationToken);

    /// <summary>POST /analitica/agrupamiento (solo AC-02): pide recalcular; el lote corre en segundo plano.</summary>
    Task<EstadoRecalculo> RecalcularAsync(CancellationToken cancellationToken);

    /// <summary>GET /analitica/agrupamiento: estado del último recálculo y fecha del último cálculo.</summary>
    Task<EstadoRecalculo> EstadoRecalculoAsync(CancellationToken cancellationToken);

    /// <summary>POST /reportes/analitica.pdf (J-03): la analítica en PDF con el gráfico de la pantalla.</summary>
    Task<ArchivoDescargado> AnaliticaPdfAsync(SolicitudPdfAnalitica solicitud, CancellationToken cancellationToken);
}

public sealed record Caracteristicas(double? Intervenciones, double? ProporcionCorrectivos, double? CostoConfirmado, double? TiempoMedioMin);

public sealed record GrupoAnalitica(int Grupo, int Equipos, Caracteristicas Promedio, string? Nombre = null);

public sealed record EquipoAgrupado(Guid EquipoId, string? PlacaInventario, int Grupo, Caracteristicas Caracteristicas, string? TipoEquipo = null);

/// <summary>Una agrupación con k grupos (de 2 a 6) y su inercia.</summary>
public sealed record AgrupacionAnalitica(int K, double? Inercia, IReadOnlyList<GrupoAnalitica> Grupos, IReadOnlyList<EquipoAgrupado> Equipos);

/// <summary>Respuesta de GET /analitica/agrupamientos (AgrupamientoDto).</summary>
public sealed record Agrupamiento(
    DateTimeOffset GeneradoEn,
    DateTimeOffset FechaCorte,
    int IntervencionesAnalizadas,
    int K,
    IReadOnlyList<GrupoAnalitica> Grupos,
    IReadOnlyList<EquipoAgrupado> Equipos,
    IReadOnlyList<AgrupacionAnalitica>? Agrupaciones = null);

/// <summary>Estado del recálculo: sin_solicitud, en_curso, terminado o fallido.</summary>
public sealed record EstadoRecalculo(string Estado, DateTimeOffset? IniciadoEn, DateTimeOffset? TerminadoEn, string? Mensaje, DateTimeOffset? UltimoCalculo)
{
    public const string EnCurso = "en_curso";
    public const string Terminado = "terminado";
    public const string Fallido = "fallido";
}

public sealed class ServicioAnaliticaHttp(ClienteApi cliente) : IServicioAnalitica
{
    private const string Recalculo = "analitica/agrupamiento";

    public Task<Agrupamiento> AgrupamientosAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Agrupamiento>(cliente, HttpMethod.Get, "analitica/agrupamientos", null, Mensaje, cancellationToken);

    public Task<EstadoRecalculo> RecalcularAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<EstadoRecalculo>(cliente, HttpMethod.Post, Recalculo, null, MensajeRecalculo, cancellationToken);

    public Task<EstadoRecalculo> EstadoRecalculoAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<EstadoRecalculo>(cliente, HttpMethod.Get, Recalculo, null, Mensaje, cancellationToken);

    public Task<ArchivoDescargado> AnaliticaPdfAsync(SolicitudPdfAnalitica solicitud, CancellationToken cancellationToken) =>
        PeticionApi.DescargarAsync(cliente, HttpMethod.Post, "reportes/analitica.pdf", solicitud, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado == 403 ? "Tu rol no permite consultar la analítica." : null;

    private static string? MensajeRecalculo(int estado) => estado == 403 ? "Solo el ingeniero biomédico puede recalcular la analítica." : null;
}
