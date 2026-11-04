namespace Sitbio.Application.Auditoria;

/// <summary>
/// Registra un evento en la bitácora con la hora del servidor y la IP de origen (RF-013).
/// Nunca interrumpe la operación auditada: si la bitácora falla, deja constancia en el log
/// técnico y sigue. El detalle no lleva contraseñas, tokens ni datos personales.
/// </summary>
public interface IRegistroAuditoria
{
    Task RegistrarAsync(
        Guid usuarioId,
        string accion,
        string entidad,
        string? entidadId = null,
        IReadOnlyDictionary<string, string>? detalle = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Datos de la solicitud HTTP actual que se guardan en la bitácora.</summary>
public interface IOrigenSolicitud
{
    string? IpOrigen { get; }
}
