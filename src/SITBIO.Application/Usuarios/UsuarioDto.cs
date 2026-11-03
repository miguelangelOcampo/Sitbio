using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Usuarios;

/// <summary>Usuario tal como lo ve el Administrador (API-USR). Nunca incluye el hash de la contraseña.</summary>
public sealed record UsuarioDto(
    Guid Id,
    string Nombres,
    string Apellidos,
    string Correo,
    string? Telefono,
    string Rol,
    string Estado,
    bool DebeCambiarClave,
    DateTimeOffset? UltimoAcceso)
{
    public const string Activo = "Activo";
    public const string Inactivo = "Inactivo";
    public const string Bloqueado = "Bloqueado";

    public static UsuarioDto Desde(Usuario usuario, string codigoRol) => new(
        usuario.Id,
        usuario.Nombres,
        usuario.Apellidos,
        usuario.Correo,
        usuario.Telefono,
        codigoRol,
        !usuario.EstadoActivo ? Inactivo : usuario.EstadoBloqueo ? Bloqueado : Activo,
        usuario.CambioContrasenaObligatorio,
        usuario.UltimoAcceso);
}

/// <summary>Datos que el Administrador envía al crear o editar un usuario; el rol va por código (AC-01 a AC-07).</summary>
public sealed record DatosUsuario(string Nombres, string Apellidos, string Correo, string? Telefono, string Rol);

/// <summary>Respuesta al crear: la clave temporal se muestra una sola vez y no se guarda en claro.</summary>
public sealed record UsuarioCreado(UsuarioDto Usuario, string ClaveTemporal);

/// <summary>Rol que se puede asignar: id interno y código estable (V004).</summary>
public sealed record RolAsignable(int Id, string Codigo);
