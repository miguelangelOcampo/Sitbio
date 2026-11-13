using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// API-TIQ: reporte de fallas y catálogo de tipos de falla para UI-009 (HU-003, HU-018); lista, consulta
/// por código e historial para el seguimiento UI-010 (HU-017).
/// </summary>
public interface IServicioTiquetes
{
    /// <summary>GET /tiquetes/tipos-falla: nombres de los tipos de falla activos de la institución.</summary>
    Task<IReadOnlyList<string>> TiposFallaAsync(CancellationToken cancellationToken);

    /// <summary>POST /tiquetes: crea el tiquete y devuelve su código TQT-######.</summary>
    Task<Tiquete> ReportarAsync(DatosTiquete datos, CancellationToken cancellationToken);

    /// <summary>GET /tiquetes: página de tiquetes del más reciente al más antiguo. La API limita al Personal Clínico a los suyos.</summary>
    Task<PaginaTiquetes> ListarAsync(FiltroTiquetes filtro, CancellationToken cancellationToken);

    /// <summary>GET /tiquetes/{codigo}: el tiquete con su estado actual.</summary>
    Task<Tiquete> ObtenerAsync(string codigo, CancellationToken cancellationToken);

    /// <summary>GET /tiquetes/{codigo}/historial: cambios de estado, del más antiguo al más reciente.</summary>
    Task<IReadOnlyList<CambioTiquete>> HistorialAsync(string codigo, CancellationToken cancellationToken);
}

/// <summary>Estados del tiquete (script 01) con su nombre para mostrar.</summary>
public static class EstadosTiquete
{
    public const string Recibido = "recibido";
    public const string Asignado = "asignado";
    public const string EnEjecucion = "en_ejecucion";
    public const string Cerrado = "cerrado";

    public static IReadOnlyList<string> Todos { get; } = [Recibido, Asignado, EnEjecucion, Cerrado];

    public static string Nombre(string? estado) => estado switch
    {
        Recibido => "Recibido",
        Asignado => "Asignado",
        EnEjecucion => "En ejecución",
        Cerrado => "Cerrado",
        _ => estado ?? string.Empty,
    };
}

/// <summary>Filtros de GET /tiquetes: estado y «mios» (solo lo que reportó quien consulta).</summary>
public sealed record FiltroTiquetes(string? Estado = null, bool Mios = false, int Pagina = 1, int Tamano = 20);

public sealed record PaginaTiquetes(IReadOnlyList<Tiquete> Items, int Page, int PageSize, int Total);

/// <summary>Un cambio del historial del tiquete (HistorialTiqueteDto). La recepción no tiene estado anterior.</summary>
public sealed record CambioTiquete(string? EstadoAnterior, string NuevoEstado, string? Comentario, DateTimeOffset FechaCambio, Guid UsuarioId, string Usuario);

/// <summary>Valores de criticidad (script 01; RF-018).</summary>
public static class Criticidades
{
    public const string Critica = "critica";
    public const string Alta = "alta";
    public const string Media = "media";
    public const string Baja = "baja";
}

/// <summary>Cuerpo de POST /tiquetes. La descripción es opcional (DOC-05).</summary>
public sealed record DatosTiquete(Guid EquipoId, string TipoFalla, string? Descripcion, string Criticidad);

/// <summary>Tiquete tal como lo devuelve la API (TiqueteDto).</summary>
public sealed record Tiquete(
    Guid Id,
    string Codigo,
    Guid EquipoId,
    string PlacaInventario,
    string NumeroSerie,
    string TipoEquipo,
    string Ubicacion,
    string TipoFalla,
    string Descripcion,
    string Criticidad,
    string PrioridadSugerida,
    string Estado,
    DateTimeOffset FechaReporte,
    DateTimeOffset? FechaCierre,
    Guid ReportanteId,
    string Reportante,
    string EstadoEquipo,
    bool EquipoFueraDeServicio);

public sealed class ServicioTiquetesHttp(ClienteApi cliente) : IServicioTiquetes
{
    public Task<IReadOnlyList<string>> TiposFallaAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<IReadOnlyList<string>>(cliente, HttpMethod.Get, "tiquetes/tipos-falla", null, Mensaje, cancellationToken);

    public Task<Tiquete> ReportarAsync(DatosTiquete datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Tiquete>(cliente, HttpMethod.Post, "tiquetes", datos, Mensaje, cancellationToken);

    public Task<PaginaTiquetes> ListarAsync(FiltroTiquetes filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var ruta = $"tiquetes?page={filtro.Pagina}&pageSize={filtro.Tamano}";
        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            ruta += $"&estado={Uri.EscapeDataString(filtro.Estado)}";
        }

        if (filtro.Mios)
        {
            ruta += "&mios=true";
        }

        return PeticionApi.EnviarAsync<PaginaTiquetes>(cliente, HttpMethod.Get, ruta, null, MensajeConsulta, cancellationToken);
    }

    public Task<Tiquete> ObtenerAsync(string codigo, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Tiquete>(cliente, HttpMethod.Get, $"tiquetes/{Uri.EscapeDataString(codigo)}", null, MensajeConsulta, cancellationToken);

    public Task<IReadOnlyList<CambioTiquete>> HistorialAsync(string codigo, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<IReadOnlyList<CambioTiquete>>(
            cliente, HttpMethod.Get, $"tiquetes/{Uri.EscapeDataString(codigo)}/historial", null, MensajeConsulta, cancellationToken);

    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite reportar fallas.",
        404 => "Ese equipo ya no está en el inventario de tu institución. Búscalo de nuevo.",
        >= 500 => "El servidor no pudo registrar el reporte. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    /// <summary>404 también cubre los tiquetes de otra institución y, para el Personal Clínico, los de otras personas.</summary>
    private static string? MensajeConsulta(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar tiquetes.",
        404 => "No encontramos un tiquete con ese código entre los que puedes consultar. Revisa el código.",
        >= 500 => "El servidor no pudo consultar los tiquetes. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
