namespace Sitbio.Desktop.Services;

/// <summary>Gestión de usuarios del Administrador (API-USR, HU-011). El permiso lo valida la API (RNF-003).</summary>
public interface IServicioUsuarios
{
    /// <summary>Todos los usuarios de la institución del token (recorre las páginas de la API).</summary>
    Task<IReadOnlyList<UsuarioResumen>> ListarAsync(CancellationToken cancellationToken);

    Task<UsuarioCreado> CrearAsync(DatosUsuario datos, CancellationToken cancellationToken);

    Task<UsuarioResumen> ActualizarAsync(Guid id, DatosUsuario datos, CancellationToken cancellationToken);

    /// <summary>Acción: «desactivar», «reactivar» o «desbloquear».</summary>
    Task<UsuarioResumen> CambiarEstadoAsync(Guid id, string accion, CancellationToken cancellationToken);

    /// <summary>RF-012: nueva clave temporal; la API la devuelve una sola vez.</summary>
    Task<string> AsignarClaveTemporalAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record UsuarioResumen(
    Guid Id,
    string Nombres,
    string Apellidos,
    string Correo,
    string? Telefono,
    string Rol,
    string Estado,
    bool DebeCambiarClave,
    DateTimeOffset? UltimoAcceso);

public sealed record DatosUsuario(string Nombres, string Apellidos, string Correo, string? Telefono, string Rol);

public sealed record UsuarioCreado(UsuarioResumen Usuario, string ClaveTemporal);

/// <summary>
/// Respuesta de error de la API (Problem Details): código de negocio (USR-001, RN-009…),
/// mensaje para el usuario y errores por campo (400). Sin conexión: <see cref="SinConexion"/>.
/// </summary>
public sealed class ErrorApi(string? codigo, string mensaje, IReadOnlyDictionary<string, string> errores, bool sinConexion = false)
    : Exception(mensaje)
{
    public string? Codigo { get; } = codigo;

    public IReadOnlyDictionary<string, string> Errores { get; } = errores;

    public bool SinConexion { get; } = sinConexion;
}
