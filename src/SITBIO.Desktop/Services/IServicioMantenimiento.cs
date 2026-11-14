using System.Globalization;
using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// API-PLAN, API-ALERTAS y API-CAL (HU-019, HU-020, HU-022): planes preventivos para UI-015 (crea y modifica solo
/// AC-02; consultan AC-01, AC-02, AC-03, AC-05 y AC-07), el centro de alertas UI-016 (AC-02 y AC-03) y las
/// calibraciones de UI-017 (registra y corrige AC-03, aprueba AC-02). Al consultar las alertas, la API genera las
/// órdenes preventivas de los planes cuya fecha llegó.
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

    /// <summary>GET /calibraciones, las más recientes primero.</summary>
    Task<PaginaCalibraciones> ListarCalibracionesAsync(FiltroCalibraciones filtro, CancellationToken cancellationToken);

    /// <summary>POST /calibraciones: sin vencimiento, la API lo calcula con la periodicidad del equipo (RN-010).</summary>
    Task<Calibracion> RegistrarCalibracionAsync(DatosCalibracion datos, CancellationToken cancellationToken);

    /// <summary>PATCH /calibraciones/{id}: corrige una calibración sin aprobar; null = no cambia.</summary>
    Task<Calibracion> CorregirCalibracionAsync(Guid id, DatosCorreccionCalibracion datos, CancellationToken cancellationToken);

    /// <summary>PATCH /calibraciones/{id}/aprobacion: el Ingeniero aprueba el registro.</summary>
    Task<Calibracion> AprobarCalibracionAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Resultados admitidos de una calibración (RegistroCalibracion en el dominio).</summary>
public static class ResultadosCalibracion
{
    public const string Conforme = "conforme";
    public const string NoConforme = "no conforme";

    public static string Nombre(string resultado) => resultado switch
    {
        Conforme => "Conforme",
        NoConforme => "No conforme",
        _ => resultado,
    };
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

/// <summary>Filtros de GET /calibraciones; null = todos los equipos.</summary>
public sealed record FiltroCalibraciones(Guid? EquipoId = null, int Pagina = 1, int Tamano = 20);

public sealed record PaginaCalibraciones(IReadOnlyList<Calibracion> Items, int Page, int PageSize, int Total);

/// <summary>Calibración tal como la devuelve la API (CalibracionDto).</summary>
public sealed record Calibracion(
    Guid Id,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    Guid? ProveedorId,
    string? Proveedor,
    DateOnly FechaCalibracion,
    string PatronReferencia,
    string Resultado,
    string? NumeroCertificado,
    DateOnly FechaVencimiento,
    bool Vigente,
    Guid RegistradoPorId,
    string RegistradoPor,
    Guid? AprobadoPorId,
    string? AprobadoPor,
    bool Aprobada);

/// <summary>Cuerpo de POST /calibraciones.</summary>
public sealed record DatosCalibracion(
    Guid EquipoId,
    DateOnly FechaCalibracion,
    string PatronReferencia,
    string Resultado,
    string? NumeroCertificado,
    DateOnly? FechaVencimiento,
    Guid? ProveedorId = null);

/// <summary>Cuerpo de PATCH /calibraciones/{id}; null = no cambia (un certificado vacío lo quita).</summary>
public sealed record DatosCorreccionCalibracion(
    DateOnly? FechaCalibracion = null,
    string? PatronReferencia = null,
    string? Resultado = null,
    string? NumeroCertificado = null,
    DateOnly? FechaVencimiento = null);

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

    public Task<PaginaCalibraciones> ListarCalibracionesAsync(FiltroCalibraciones filtro, CancellationToken cancellationToken)
    {
        var consulta = $"page={filtro.Pagina}&pageSize={filtro.Tamano}" + (filtro.EquipoId is { } equipo ? $"&equipoId={equipo}" : string.Empty);
        return PeticionApi.EnviarAsync<PaginaCalibraciones>(cliente, HttpMethod.Get, $"calibraciones?{consulta}", null, MensajeConsultaCalibraciones, cancellationToken);
    }

    public Task<Calibracion> RegistrarCalibracionAsync(DatosCalibracion datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Calibracion>(cliente, HttpMethod.Post, "calibraciones", datos, MensajeRegistroCalibracion, cancellationToken);

    public Task<Calibracion> CorregirCalibracionAsync(Guid id, DatosCorreccionCalibracion datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Calibracion>(cliente, HttpMethod.Patch, $"calibraciones/{id}", datos, MensajeRegistroCalibracion, cancellationToken);

    public Task<Calibracion> AprobarCalibracionAsync(Guid id, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Calibracion>(cliente, HttpMethod.Patch, $"calibraciones/{id}/aprobacion", null, MensajeAprobacionCalibracion, cancellationToken);

    private static string? MensajeConsultaCalibraciones(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar las calibraciones.",
        >= 500 => "El servidor no pudo consultar las calibraciones. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    /// <summary>400 y 409 traen el mensaje de la API (equipo que no requiere calibración, ya aprobada), que explica qué pasó.</summary>
    private static string? MensajeRegistroCalibracion(int estado) => estado switch
    {
        403 => "Solo el Técnico de Mantenimiento registra y corrige las calibraciones.",
        404 => "Ese equipo o calibración ya no está en tu institución. Actualiza la lista.",
        >= 500 => "El servidor no pudo guardar la calibración. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajeAprobacionCalibracion(int estado) => estado switch
    {
        403 => "Solo el Ingeniero Biomédico aprueba las calibraciones.",
        404 => "Esa calibración ya no está en tu institución. Actualiza la lista.",
        >= 500 => "El servidor no pudo aprobar la calibración. Intenta de nuevo en unos minutos.",
        _ => null,
    };

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
