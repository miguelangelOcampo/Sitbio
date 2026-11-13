using System.Net.Http;
using System.Text.Json;

namespace Sitbio.Desktop.Services;

/// <summary>
/// API-OT: asignación de órdenes desde la bandeja UI-011 (HU-004) y ejecución desde «Mis órdenes» UI-012 e
/// «Intervención» UI-013 (HU-005). Solo el Ingeniero Biomédico (AC-02) asigna y solo el Técnico (AC-03)
/// inicia, pausa y registra; la API valida los permisos, RN-003, RN-004 y los campos dinámicos.
/// </summary>
public interface IServicioOrdenes
{
    /// <summary>GET /ordenes/tecnicos: técnicos activos con sus órdenes abiertas, del menos al más ocupado.</summary>
    Task<IReadOnlyList<TecnicoAsignable>> TecnicosAsync(CancellationToken cancellationToken);

    /// <summary>POST /ordenes: crea la orden correctiva y deja el tiquete «asignado».</summary>
    Task<Orden> AsignarAsync(DatosOrden datos, CancellationToken cancellationToken);

    /// <summary>GET /ordenes: de la más urgente a la menos urgente; con <c>AsignadasAMi</c>, solo las del técnico.</summary>
    Task<PaginaOrdenes> ListarAsync(FiltroOrdenes filtro, CancellationToken cancellationToken);

    /// <summary>PATCH /ordenes/{id}/estado: «iniciar» o «pausar» (solo el técnico asignado).</summary>
    Task<Orden> CambiarEstadoAsync(Guid ordenId, string accion, CancellationToken cancellationToken);

    /// <summary>GET /ordenes/{id}/intervencion; null si aún no se registró.</summary>
    Task<Intervencion?> ObtenerIntervencionAsync(Guid ordenId, CancellationToken cancellationToken);

    /// <summary>PUT /ordenes/{id}/intervencion: registra o corrige la hoja de servicio.</summary>
    Task<Intervencion> RegistrarIntervencionAsync(Guid ordenId, DatosIntervencion datos, CancellationToken cancellationToken);
}

/// <summary>Estados de la orden de trabajo (script 01).</summary>
public static class EstadosOrden
{
    public const string Asignada = "asignada";
    public const string EnEjecucion = "en_ejecucion";
    public const string Pausada = "pausada";
    public const string Cerrada = "cerrada";
    public const string Aprobada = "aprobada";

    public static IReadOnlyList<string> Todos { get; } = [Asignada, EnEjecucion, Pausada, Cerrada, Aprobada];

    public static string Nombre(string? estado) => estado switch
    {
        Asignada => "Asignada",
        EnEjecucion => "En ejecución",
        Pausada => "Pausada",
        Cerrada => "Cerrada",
        Aprobada => "Aprobada",
        _ => estado ?? string.Empty,
    };
}

/// <summary>Acciones del técnico sobre su orden (API-OT-ESTADO).</summary>
public static class AccionesOrden
{
    public const string Iniciar = "iniciar";
    public const string Pausar = "pausar";
}

/// <summary>Estado del equipo al terminar la intervención (enum de <c>intervenciones</c>).</summary>
public static class EstadosSalidaEquipo
{
    public static IReadOnlyList<string> Todos { get; } = ["Operativo", "En mantenimiento", "Fuera de servicio", "Baja"];
}

/// <summary>Técnico de Mantenimiento activo con su carga (TecnicoConCarga).</summary>
public sealed record TecnicoAsignable(Guid Id, string Nombre, int OrdenesAbiertas);

/// <summary>Cuerpo de POST /ordenes. Sin prioridad, la API usa la sugerida por la criticidad.</summary>
public sealed record DatosOrden(Guid TiqueteId, Guid TecnicoId, string? Prioridad, DateOnly? FechaProgramada);

/// <summary>Orden de trabajo tal como la devuelve la API (OrdenDto); solo los campos que usa el cliente.</summary>
public sealed record Orden(
    Guid Id,
    Guid? TiqueteId,
    string? CodigoTiquete,
    Guid TecnicoId,
    string Tecnico,
    string TipoMantenimiento,
    string Prioridad,
    string Estado,
    DateOnly? FechaProgramada,
    Guid EquipoId = default,
    string PlacaInventario = "",
    string TipoEquipo = "",
    string Ubicacion = "",
    string? TipoFalla = null,
    DateTimeOffset? FechaAsignacion = null,
    DateTimeOffset? FechaInicio = null,
    bool TieneIntervencion = false);

/// <summary>Filtros de GET /ordenes.</summary>
public sealed record FiltroOrdenes(string? Estado = null, bool AsignadasAMi = false, int Pagina = 1, int Tamano = 20);

public sealed record PaginaOrdenes(IReadOnlyList<Orden> Items, int Page, int PageSize, int Total);

public sealed record Repuesto(string Nombre, int Cantidad, decimal? CostoUnitario);

/// <summary>
/// Hoja de servicio (IntervencionDto). Los <see cref="CamposDinamicos"/> llegan como JSON con el tipo de
/// cada campo (número, texto, sí/no, fecha <c>yyyy-MM-dd</c> u opción).
/// </summary>
public sealed record Intervencion(
    Guid Id,
    Guid OrdenId,
    string Diagnostico,
    string AccionesRealizadas,
    IReadOnlyList<Repuesto> Repuestos,
    decimal? CostoManoObra,
    decimal CostoTotal,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    IReadOnlyDictionary<string, JsonElement> CamposDinamicos,
    int? VersionCampos,
    DateTimeOffset FechaIntervencion,
    bool CerradaFirmada);

/// <summary>Cuerpo de PUT /ordenes/{id}/intervencion. Cada valor dinámico va con su tipo JSON.</summary>
public sealed record DatosIntervencion(
    string Diagnostico,
    string AccionesRealizadas,
    IReadOnlyList<Repuesto> Repuestos,
    decimal? CostoManoObra,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    IReadOnlyDictionary<string, object> CamposDinamicos);

public sealed class ServicioOrdenesHttp(ClienteApi cliente) : IServicioOrdenes
{
    public Task<IReadOnlyList<TecnicoAsignable>> TecnicosAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<IReadOnlyList<TecnicoAsignable>>(cliente, HttpMethod.Get, "ordenes/tecnicos", null, MensajeTecnicos, cancellationToken);

    public Task<Orden> AsignarAsync(DatosOrden datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Orden>(cliente, HttpMethod.Post, "ordenes", datos, MensajeAsignacion, cancellationToken);

    public Task<PaginaOrdenes> ListarAsync(FiltroOrdenes filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var ruta = $"ordenes?page={filtro.Pagina}&pageSize={filtro.Tamano}";
        if (filtro.Estado is { Length: > 0 } estado)
        {
            ruta += $"&estado={Uri.EscapeDataString(estado)}";
        }

        if (filtro.AsignadasAMi)
        {
            ruta += "&asignadas=me";
        }

        return PeticionApi.EnviarAsync<PaginaOrdenes>(cliente, HttpMethod.Get, ruta, null, MensajeConsulta, cancellationToken);
    }

    public Task<Orden> CambiarEstadoAsync(Guid ordenId, string accion, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Orden>(cliente, HttpMethod.Patch, $"ordenes/{ordenId}/estado", new { accion }, MensajeEjecucion, cancellationToken);

    public async Task<Intervencion?> ObtenerIntervencionAsync(Guid ordenId, CancellationToken cancellationToken)
    {
        try
        {
            return await PeticionApi.EnviarAsync<Intervencion>(cliente, HttpMethod.Get, $"ordenes/{ordenId}/intervencion", null, MensajeConsultaIntervencion, cancellationToken);
        }
        catch (ErrorApi error) when (error.Codigo is null && error.Message == SinIntervencion)
        {
            return null;
        }
    }

    public Task<Intervencion> RegistrarIntervencionAsync(Guid ordenId, DatosIntervencion datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Intervencion>(cliente, HttpMethod.Put, $"ordenes/{ordenId}/intervencion", datos, MensajeEjecucion, cancellationToken);

    /// <summary>Marca interna del 404 de la consulta: la orden aún no tiene intervención.</summary>
    private const string SinIntervencion = "La orden aún no tiene intervención registrada.";

    private static string? MensajeTecnicos(int estado) => estado switch
    {
        403 => "Tu rol no permite asignar órdenes de trabajo.",
        >= 500 => "El servidor no pudo consultar los técnicos. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    /// <summary>400 y 409 traen el mensaje de la API (RN-003, OT-001), que explica qué pasó.</summary>
    private static string? MensajeAsignacion(int estado) => estado switch
    {
        403 => "Tu rol no permite asignar órdenes de trabajo.",
        404 => "Ese tiquete ya no está en la bandeja de tu institución. Actualiza la lista.",
        >= 500 => "El servidor no pudo crear la orden. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajeConsulta(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar órdenes de trabajo.",
        >= 500 => "El servidor no pudo consultar las órdenes. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajeConsultaIntervencion(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar intervenciones.",
        404 => SinIntervencion,
        >= 500 => "El servidor no pudo consultar la intervención. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    /// <summary>400 y 409 traen el mensaje de la API (OT-002, OT-003, RN-004); 503 = MongoDB no respondió y la orden no cambió (RT-002).</summary>
    private static string? MensajeEjecucion(int estado) => estado switch
    {
        403 => "Solo el técnico de mantenimiento ejecuta las órdenes de trabajo.",
        404 => "Esta orden ya no está asignada a ti. Actualiza la lista.",
        503 => "No se pudo guardar la intervención porque el servidor de documentos no respondió. La orden no cambió; intenta de nuevo.",
        >= 500 => "El servidor no pudo completar la operación. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
