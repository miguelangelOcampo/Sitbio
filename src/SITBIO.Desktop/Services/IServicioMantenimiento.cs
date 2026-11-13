using System.Globalization;
using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// API-PLAN y API-ALERTAS (HU-019, HU-020): planes preventivos para UI-015 (crea y modifica solo AC-02; consultan
/// AC-01, AC-02, AC-03, AC-05 y AC-07) y el centro de alertas UI-016 (AC-02 y AC-03). Al consultar las alertas,
/// la API genera las órdenes preventivas de los planes cuya fecha llegó.
/// </summary>
public interface IServicioMantenimiento
{
    /// <summary>GET /planes-preventivos, de la próxima fecha más cercana a la más lejana.</summary>
    Task<PaginaPlanes> ListarPlanesAsync(FiltroPlanes filtro, CancellationToken cancellationToken);

    /// <summary>GET /planes-preventivos/{id}.</summary>
    Task<PlanMantenimiento> ObtenerPlanAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>POST /planes-preventivos: sin próxima fecha, la API la calcula como último + periodicidad.</summary>
    Task<PlanMantenimiento> CrearPlanAsync(DatosPlan datos, CancellationToken cancellationToken);

    /// <summary>PUT /planes-preventivos/{id}: solo los campos que no son null cambian.</summary>
    Task<PlanMantenimiento> ActualizarPlanAsync(Guid id, DatosActualizacionPlan datos, CancellationToken cancellationToken);

    /// <summary>GET /alertas: los días de anticipación de la institución y lo que está por vencer o vencido.</summary>
    Task<ResumenAlertas> AlertasAsync(CancellationToken cancellationToken);
}

/// <summary>Estados del plan (PlanPreventivo en el dominio).</summary>
public static class EstadosPlan
{
    public const string Activo = "activo";
    public const string Suspendido = "suspendido";
    public const string Finalizado = "finalizado";

    public static string Nombre(string estado) => estado switch
    {
        Activo => "Activo",
        Suspendido => "Suspendido",
        Finalizado => "Finalizado",
        _ => estado,
    };
}

/// <summary>Tipos y estados de las alertas (AlertaDto).</summary>
public static class TiposAlerta
{
    public const string Preventivo = "preventivo";
    public const string Calibracion = "calibracion";
    public const string Proxima = "proxima";
    public const string Vencida = "vencida";
}

/// <summary>Filtros de GET /planes-preventivos; null = sin filtro.</summary>
public sealed record FiltroPlanes(Guid? EquipoId = null, string? Estado = null, int Pagina = 1, int Tamano = 20);

public sealed record PaginaPlanes(IReadOnlyList<PlanMantenimiento> Items, int Page, int PageSize, int Total);

/// <summary>Plan tal como lo devuelve la API (PlanDto).</summary>
public sealed record PlanMantenimiento(
    Guid Id,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string Descripcion,
    int PeriodicidadDias,
    DateOnly? UltimaEjecucion,
    DateOnly ProximaFecha,
    string Estado,
    bool Vencido,
    DateTimeOffset FechaCreacion);

/// <summary>Cuerpo de POST /planes-preventivos.</summary>
public sealed record DatosPlan(Guid EquipoId, string? Descripcion, int PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly? ProximaFecha);

/// <summary>Cuerpo de PUT /planes-preventivos/{id}; null = no cambia.</summary>
public sealed record DatosActualizacionPlan(
    string? Descripcion = null,
    int? PeriodicidadDias = null,
    DateOnly? UltimaEjecucion = null,
    DateOnly? ProximaFecha = null,
    string? Estado = null);

/// <summary>Respuesta de GET /alertas (AlertasDto).</summary>
public sealed record ResumenAlertas(int DiasAnticipacion, IReadOnlyList<Alerta> Alertas, int OrdenesGeneradas);

/// <summary>Alerta de mantenimiento preventivo o de calibración (AlertaDto).</summary>
public sealed record Alerta(
    string Tipo,
    string Estado,
    DateOnly Fecha,
    int DiasRestantes,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string Descripcion,
    Guid? PlanId,
    Guid? OrdenId,
    string? EstadoOrden)
{
    /// <summary>Identifica la alerta entre consultas, para recordar que se revisó (UI-016).</summary>
    public string Clave => string.Create(CultureInfo.InvariantCulture, $"{Tipo}:{PlanId ?? EquipoId}:{Fecha:yyyy-MM-dd}");
}

public sealed class ServicioMantenimientoHttp(ClienteApi cliente) : IServicioMantenimiento
{
    public Task<PaginaPlanes> ListarPlanesAsync(FiltroPlanes filtro, CancellationToken cancellationToken)
    {
        var consulta = new List<string> { $"page={filtro.Pagina}", $"pageSize={filtro.Tamano}" };
        if (filtro.EquipoId is { } equipo)
        {
            consulta.Add($"equipoId={equipo}");
        }

        if (filtro.Estado is { } estado)
        {
            consulta.Add($"estado={Uri.EscapeDataString(estado)}");
        }

        return PeticionApi.EnviarAsync<PaginaPlanes>(cliente, HttpMethod.Get, $"planes-preventivos?{string.Join('&', consulta)}", null, MensajeConsulta, cancellationToken);
    }

    public Task<PlanMantenimiento> ObtenerPlanAsync(Guid id, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<PlanMantenimiento>(cliente, HttpMethod.Get, $"planes-preventivos/{id}", null, MensajePlan, cancellationToken);

    public Task<PlanMantenimiento> CrearPlanAsync(DatosPlan datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<PlanMantenimiento>(cliente, HttpMethod.Post, "planes-preventivos", datos, MensajeGuardado, cancellationToken);

    public Task<PlanMantenimiento> ActualizarPlanAsync(Guid id, DatosActualizacionPlan datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<PlanMantenimiento>(cliente, HttpMethod.Put, $"planes-preventivos/{id}", datos, MensajeGuardado, cancellationToken);

    public Task<ResumenAlertas> AlertasAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ResumenAlertas>(cliente, HttpMethod.Get, "alertas", null, MensajeAlertas, cancellationToken);

    private static string? MensajeConsulta(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar los planes de mantenimiento.",
        >= 500 => "El servidor no pudo consultar los planes. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajePlan(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar los planes de mantenimiento.",
        404 => "Ese plan ya no está en tu institución.",
        >= 500 => "El servidor no pudo consultar el plan. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    /// <summary>400 y 409 traen el mensaje de la API (equipo dado de baja, plan finalizado), que explica qué pasó.</summary>
    private static string? MensajeGuardado(int estado) => estado switch
    {
        403 => "Solo el Ingeniero Biomédico crea y modifica los planes de mantenimiento.",
        404 => "Ese equipo o plan ya no está en tu institución. Actualiza la lista.",
        >= 500 => "El servidor no pudo guardar el plan. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajeAlertas(int estado) => estado switch
    {
        403 => "Tu rol no recibe alertas de vencimiento.",
        >= 500 => "El servidor no pudo consultar las alertas. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
