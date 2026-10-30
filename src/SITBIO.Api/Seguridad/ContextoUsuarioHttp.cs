using System.Security.Claims;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;

namespace Sitbio.Api.Seguridad;

/// <summary>
/// Usuario, tenant y rol del JWT de la solicitud actual (RN-008, SEC-003): el tenant
/// nunca sale del cuerpo ni de la URL. Los claims se leen al usarse, así una ruta
/// anónima puede recibir el servicio sin fallar; si intenta usarlo, responde 401.
/// </summary>
public sealed class ContextoUsuarioHttp(IHttpContextAccessor accesor) : IContextoUsuario
{
    public Guid TenantId => LeerGuid(ClaimsSitbio.Tenant);

    public Guid UsuarioId => LeerGuid(ClaimsSitbio.Usuario);

    public string Rol => Leer(ClaimsSitbio.Rol);

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
