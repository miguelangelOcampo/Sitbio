using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Sesiones emitidas (<c>sesion_token</c>) para renovar, cerrar y validar tokens (HU-012).</summary>
public interface IRepositorioSesiones
{
    /// <summary>
    /// ¿El token sigue sin revocar y su usuario tiene clave temporal? Se usa al validar cada
    /// solicitud, antes de que exista el usuario de la solicitud, por eso recibe el tenant del
    /// token en lugar de usar el filtro global. Una sola consulta (HU-012, HU-013).
    /// </summary>
    Task<EstadoSesion> ConsultarEstadoAsync(Guid tenantId, string jti, CancellationToken cancellationToken);

    /// <summary>Sesiones sin revocar ni expirar del usuario (del tenant del token), para revocarlas (HU-013).</summary>
    Task<IReadOnlyList<SesionToken>> VigentesDelUsuarioAsync(Guid usuarioId, DateTimeOffset momento, CancellationToken cancellationToken);

    /// <summary>Sesión del tenant del token con ese jti; null si no existe.</summary>
    Task<SesionToken?> ObtenerAsync(string jti, CancellationToken cancellationToken);

    /// <summary>Usuario del tenant del token con su rol y si la institución está activa; null si no existe.</summary>
    Task<UsuarioParaAcceso?> BuscarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken);

    void Agregar(SesionToken sesion);

    Task GuardarAsync(CancellationToken cancellationToken);
}

/// <summary>Estado de la sesión de un token al validar cada solicitud.</summary>
public enum EstadoSesion
{
    /// <summary>Revocada, inexistente o de otro tenant: el token se rechaza con 401.</summary>
    Cerrada,

    /// <summary>Vigente y con contraseña definida por el usuario.</summary>
    Activa,

    /// <summary>Vigente, pero el usuario debe cambiar su clave temporal antes de continuar (HU-013).</summary>
    ClaveTemporal,
}
