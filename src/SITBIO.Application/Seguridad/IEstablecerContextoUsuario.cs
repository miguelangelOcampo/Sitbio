namespace Sitbio.Application.Seguridad;

/// <summary>
/// Fija la identidad de la solicitud cuando aún no hay token: solo el inicio de sesión
/// lo usa, con el tenant que el servidor encontró por el correo, nunca con datos del cliente.
/// </summary>
public interface IEstablecerContextoUsuario
{
    void Establecer(Guid tenantId, Guid usuarioId, string rol);
}
