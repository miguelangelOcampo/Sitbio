using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// API-OT: asignación de órdenes de trabajo desde la bandeja UI-011 (HU-004). Solo el Ingeniero
/// Biomédico (AC-02) asigna; la API valida RN-003 y responde 409 si el tiquete ya tiene orden.
/// </summary>
public interface IServicioOrdenes
{
    /// <summary>GET /ordenes/tecnicos: técnicos activos con sus órdenes abiertas, del menos al más ocupado.</summary>
    Task<IReadOnlyList<TecnicoAsignable>> TecnicosAsync(CancellationToken cancellationToken);

    /// <summary>POST /ordenes: crea la orden correctiva y deja el tiquete «asignado».</summary>
    Task<Orden> AsignarAsync(DatosOrden datos, CancellationToken cancellationToken);
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
    DateOnly? FechaProgramada);

public sealed class ServicioOrdenesHttp(ClienteApi cliente) : IServicioOrdenes
{
    public Task<IReadOnlyList<TecnicoAsignable>> TecnicosAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<IReadOnlyList<TecnicoAsignable>>(cliente, HttpMethod.Get, "ordenes/tecnicos", null, MensajeTecnicos, cancellationToken);

    public Task<Orden> AsignarAsync(DatosOrden datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Orden>(cliente, HttpMethod.Post, "ordenes", datos, MensajeAsignacion, cancellationToken);

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
}
