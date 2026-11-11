using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Cuerpo de API-AUTH-PASSWORD: la contraseña actual (o la temporal) y la nueva.</summary>
public sealed record SolicitudCambioContrasena(string? Actual, string? Nueva);

/// <summary>
/// Cambio de la propia contraseña (API-AUTH-PASSWORD, RF-012, HU-013). El usuario sale del
/// token; la nueva contraseña cumple <see cref="PoliticaContrasena"/> y se guarda solo su hash
/// (RNF-004). Las demás sesiones del usuario se revocan y la actual sigue sirviendo (SEC-002).
/// </summary>
public sealed class CambiarContrasena(
    IRepositorioSesiones repositorio,
    IHasherContrasenas hasher,
    IRegistroAuditoria auditoria,
    TimeProvider tiempo)
{
    public const string MensajeActualIncorrecta = "La contraseña actual no es correcta.";
    public const string MensajeIgualALaActual = "La nueva contraseña debe ser diferente de la actual.";
    public const string DetalleObligatorio = "Cambio obligatorio de la contraseña temporal";
    public const string DetalleVoluntario = "Cambio voluntario de contraseña";

    public async Task EjecutarAsync(Guid usuarioId, string? jti, SolicitudCambioContrasena solicitud, CancellationToken cancellationToken)
    {
        var acceso = await repositorio.BuscarUsuarioAsync(usuarioId, cancellationToken);
        if (acceso is null || !acceso.Usuario.PuedeIniciarSesion || !acceso.InstitucionActiva)
        {
            throw new ErrorNoAutenticado(IniciarSesion.MensajeAccesoNoDisponible, "MSG-002");
        }

        var usuario = acceso.Usuario;
        var actual = solicitud.Actual ?? string.Empty;
        var nueva = solicitud.Nueva ?? string.Empty;
        Validar(usuario, actual, nueva);

        var obligatorio = usuario.CambioContrasenaObligatorio;
        usuario.CambiarContrasena(hasher.Hash(nueva));

        var ahora = tiempo.GetUtcNow();
        foreach (var sesion in await repositorio.VigentesDelUsuarioAsync(usuario.Id, ahora, cancellationToken))
        {
            if (sesion.Jti != jti)
            {
                sesion.Revocar(ahora, MotivosRevocacion.CambioContrasena);
            }
        }

        await repositorio.GuardarAsync(cancellationToken);

        // Sin la contraseña ni el correo en el detalle (Ley 1581 de 2012).
        await auditoria.RegistrarAsync(
            usuario.Id,
            AccionesAuditoria.Actualizar,
            "usuario",
            usuario.Id.ToString(),
            new Dictionary<string, string> { ["cambio"] = obligatorio ? DetalleObligatorio : DetalleVoluntario },
            cancellationToken);
    }

    private void Validar(Usuario usuario, string actual, string nueva)
    {
        var errores = new Dictionary<string, string[]>();
        if (actual.Length == 0)
        {
            errores["actual"] = [usuario.CambioContrasenaObligatorio ? "Escribe la contraseña temporal que recibiste." : "Escribe tu contraseña actual."];
        }
        else if (!hasher.Verificar(actual, usuario.ContrasenaHash))
        {
            errores["actual"] = [MensajeActualIncorrecta];
        }

        if (nueva.Length == 0)
        {
            errores["nueva"] = ["Escribe la nueva contraseña."];
        }
        else
        {
            var incumplidas = PoliticaContrasena.Incumplidas(nueva, usuario.Nombres, usuario.Apellidos, usuario.Correo);
            if (incumplidas.Count > 0)
            {
                errores["nueva"] = [.. incumplidas];
            }
            else if (nueva == actual)
            {
                errores["nueva"] = [MensajeIgualALaActual];
            }
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }
    }
}
