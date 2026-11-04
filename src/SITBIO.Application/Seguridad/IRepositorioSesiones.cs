using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Sesiones emitidas (<c>sesion_token</c>) para renovar, cerrar y validar tokens (HU-012).</summary>
public interface IRepositorioSesiones
{
    /// <summary>
    /// ¿El token sigue sin revocar? Se usa al validar cada solicitud, antes de que exista el
    /// usuario de la solicitud, por eso recibe el tenant del token en lugar de usar el filtro global.
    /// </summary>
    Task<bool> EstaActivaAsync(Guid tenantId, string jti, CancellationToken cancellationToken);

    /// <summary>Sesión del tenant del token con ese jti; null si no existe.</summary>
    Task<SesionToken?> ObtenerAsync(string jti, CancellationToken cancellationToken);

    /// <summary>Usuario del tenant del token con su rol y si la institución está activa; null si no existe.</summary>
    Task<UsuarioParaAcceso?> BuscarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken);

    void Agregar(SesionToken sesion);

    Task GuardarAsync(CancellationToken cancellationToken);
}
