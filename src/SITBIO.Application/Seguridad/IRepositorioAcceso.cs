using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Datos del inicio de sesión (HU-001).</summary>
public interface IRepositorioAcceso
{
    /// <summary>
    /// Busca el usuario por correo en TODOS los tenants (el correo es único, V005), con su rol.
    /// Es la única consulta que no filtra por tenant: todavía no hay token.
    /// </summary>
    Task<UsuarioParaAcceso?> BuscarPorCorreoAsync(string correo, CancellationToken cancellationToken);

    /// <summary>Guarda los cambios del usuario y, si hay, la sesión nueva.</summary>
    Task GuardarAsync(Usuario usuario, SesionToken? sesion, CancellationToken cancellationToken);
}

public sealed record UsuarioParaAcceso(Usuario Usuario, string CodigoRol, bool InstitucionActiva);
