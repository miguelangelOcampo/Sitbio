namespace Sitbio.Application.Comun;

/// <summary>
/// Datos del usuario autenticado. Se obtienen del token JWT en la API;
/// nunca del cuerpo de la solicitud (RN-008, SEC-003).
/// </summary>
public interface IContextoUsuario
{
    Guid TenantId { get; }

    Guid UsuarioId { get; }

    string Rol { get; }
}
