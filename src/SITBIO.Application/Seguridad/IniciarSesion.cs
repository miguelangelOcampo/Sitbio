using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

public sealed record SolicitudInicioSesion(string Correo, string Contrasena);

public sealed record ResultadoInicioSesion(string Token, DateTimeOffset ExpiraEn, string Rol, bool DebeCambiarClave);

/// <summary>
/// Inicio de sesión (HU-001, CU-001, RN-006). Las respuestas de error no revelan si el
/// correo existe: credenciales inválidas dan MSG-001 y una cuenta que no puede entrar, MSG-002.
/// </summary>
public sealed class IniciarSesion(
    IRepositorioAcceso repositorio,
    IHasherContrasenas hasher,
    IGeneradorTokens generador,
    IEstablecerContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string MensajeCredenciales = "Credenciales incorrectas. Verifique sus datos e intente de nuevo.";
    public const string MensajeAccesoNoDisponible =
        "No fue posible iniciar sesión. Si el problema continúa, comuníquese con el administrador del sistema.";

    // Hash de una clave aleatoria: cuando el correo no existe se verifica contra él, para que
    // la respuesta tarde lo mismo que con un correo real.
    private static string? _hashFicticio;

    public async Task<ResultadoInicioSesion> EjecutarAsync(SolicitudInicioSesion solicitud, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Correo) || string.IsNullOrEmpty(solicitud.Contrasena))
        {
            throw new ErrorValidacion(new Dictionary<string, string[]>
            {
                ["correo"] = string.IsNullOrWhiteSpace(solicitud.Correo) ? ["Ingrese el correo."] : [],
                ["contrasena"] = string.IsNullOrEmpty(solicitud.Contrasena) ? ["Ingrese la contraseña."] : [],
            }.Where(e => e.Value.Length > 0).ToDictionary());
        }

        var correo = solicitud.Correo.Trim().ToLowerInvariant();
        var acceso = await repositorio.BuscarPorCorreoAsync(correo, cancellationToken);
        if (acceso is null)
        {
            hasher.Verificar(solicitud.Contrasena, HashFicticio());
            throw CredencialesIncorrectas();
        }

        var usuario = acceso.Usuario;
        if (!usuario.PuedeIniciarSesion || !acceso.InstitucionActiva)
        {
            hasher.Verificar(solicitud.Contrasena, HashFicticio());
            throw new ErrorNoAutenticado(MensajeAccesoNoDisponible, "MSG-002");
        }

        // El tenant lo determina el servidor a partir del correo, nunca el cliente (RN-008).
        contexto.Establecer(usuario.TenantId, usuario.Id, acceso.CodigoRol);

        if (!hasher.Verificar(solicitud.Contrasena, usuario.ContrasenaHash))
        {
            usuario.RegistrarIntentoFallido();
            await repositorio.GuardarAsync(usuario, null, cancellationToken);
            throw CredencialesIncorrectas();
        }

        usuario.RegistrarAccesoExitoso(tiempo.GetUtcNow());
        var emitido = generador.Generar(usuario, acceso.CodigoRol);
        var sesion = new SesionToken(usuario.TenantId, usuario.Id, emitido.Jti, emitido.Emision, emitido.Expiracion);
        await repositorio.GuardarAsync(usuario, sesion, cancellationToken);

        return new ResultadoInicioSesion(emitido.Token, emitido.Expiracion, acceso.CodigoRol, usuario.CambioContrasenaObligatorio);
    }

    private static ErrorNoAutenticado CredencialesIncorrectas() => new(MensajeCredenciales, "MSG-001");

    private string HashFicticio() => _hashFicticio ??= hasher.Hash(Guid.NewGuid().ToString());
}
