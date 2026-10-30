using System.Security.Claims;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Seguridad;

/// <summary>
/// Usuario, tenant y rol del JWT de la solicitud actual (RN-008, SEC-003): el tenant
/// nunca sale del cuerpo ni de la URL. Los claims se leen al usarse, así una ruta
/// anónima puede recibir el servicio sin fallar; si intenta usarlo, responde 401.
/// En el inicio de sesión, que aún no tiene token, el servidor fija la identidad que
/// encontró por el correo (<see cref="Establecer"/>).
/// </summary>
public sealed class ContextoUsuarioHttp(IHttpContextAccessor accesor) : IContextoUsuario, IEstablecerContextoUsuario
{
    private (Guid Tenant, Guid Usuario, string Rol)? _establecido;

    public Guid TenantId => _establecido?.Tenant ?? LeerGuid(ClaimsSitbio.Tenant);

    public Guid UsuarioId => _establecido?.Usuario ?? LeerGuid(ClaimsSitbio.Usuario);

    public string Rol => _establecido?.Rol ?? Leer(ClaimsSitbio.Rol);

    public void Establecer(Guid tenantId, Guid usuarioId, string rol)
    {
        if (tenantId == Guid.Empty || usuarioId == Guid.Empty || string.IsNullOrWhiteSpace(rol))
        {
            throw new ArgumentException("La identidad establecida está incompleta.");
        }

        if (_establecido is { } actual && actual.Tenant != tenantId)
        {
            throw new InvalidOperationException("La solicitud ya tiene otra institución establecida.");
        }

        _establecido = (tenantId, usuarioId, rol);
    }

    private string Leer(string tipo)
    {
        var usuario = accesor.HttpContext?.User;
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            throw new ErrorNoAutenticado();
        }

        var valor = usuario.FindFirstValue(tipo);
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ErrorNoAutenticado("La sesión no es válida. Inicie sesión de nuevo.");
        }

        return valor;
    }

    private Guid LeerGuid(string tipo) =>
        Guid.TryParse(Leer(tipo), out var valor) && valor != Guid.Empty
            ? valor
            : throw new ErrorNoAutenticado("La sesión no es válida. Inicie sesión de nuevo.");
}
