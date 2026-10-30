namespace Sitbio.Desktop.Services;

/// <summary>Inicio de sesión contra la API (API-AUTH-LOGIN).</summary>
public interface IServicioAutenticacion
{
    Task<ResultadoLogin> IniciarSesionAsync(string correo, string contrasena, CancellationToken cancellationToken);
}

/// <summary>Sesión abierta: el token se adjunta a las solicitudes siguientes (commit 11).</summary>
public sealed record SesionUsuario(string Token, DateTimeOffset ExpiraEn, string Rol, bool DebeCambiarClave);

public enum FalloLogin
{
    /// <summary>MSG-001: correo o contraseña incorrectos.</summary>
    CredencialesIncorrectas,

    /// <summary>MSG-002: la cuenta no puede entrar (bloqueada, inactiva o institución inactiva).</summary>
    AccesoNoDisponible,

    /// <summary>La API limitó los intentos (429).</summary>
    DemasiadosIntentos,

    /// <summary>MSG-011: no hubo respuesta del servidor.</summary>
    SinConexion,

    Inesperado,
}

public sealed record ResultadoLogin(SesionUsuario? Sesion, FalloLogin? Fallo)
{
    public static ResultadoLogin Exitoso(SesionUsuario sesion) => new(sesion, null);

    public static ResultadoLogin Fallido(FalloLogin fallo) => new(null, fallo);
}
