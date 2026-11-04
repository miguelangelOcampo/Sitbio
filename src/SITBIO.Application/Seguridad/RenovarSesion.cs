using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>
/// Renovación del token mientras el usuario está activo (API-AUTH-REFRESH, SEC-002): revoca
/// el jti actual y emite un JWT nuevo de 30 minutos con otro jti registrado en sesion_token.
/// Si la cuenta o la institución dejaron de estar disponibles, cierra la sesión (MSG-002).
/// </summary>
public sealed class RenovarSesion(IRepositorioSesiones repositorio, IGeneradorTokens generador, TimeProvider tiempo)
{
    public async Task<ResultadoInicioSesion> EjecutarAsync(Guid usuarioId, string? jti, CancellationToken cancellationToken)
    {
        var ahora = tiempo.GetUtcNow();
        var sesion = string.IsNullOrWhiteSpace(jti) ? null : await repositorio.ObtenerAsync(jti, cancellationToken);
        if (sesion is null || sesion.UsuarioId != usuarioId || !sesion.EstaVigente(ahora))
        {
            throw new ErrorNoAutenticado();
        }

        var acceso = await repositorio.BuscarUsuarioAsync(usuarioId, cancellationToken);
        if (acceso is null || !acceso.Usuario.PuedeIniciarSesion || !acceso.InstitucionActiva)
        {
            sesion.Revocar(ahora, MotivosRevocacion.AccesoNoDisponible);
            await repositorio.GuardarAsync(cancellationToken);
            throw new ErrorNoAutenticado(IniciarSesion.MensajeAccesoNoDisponible, "MSG-002");
        }

        var usuario = acceso.Usuario;
        sesion.Revocar(ahora, MotivosRevocacion.Renovacion);
        var emitido = generador.Generar(usuario, acceso.CodigoRol);
        repositorio.Agregar(new SesionToken(usuario.TenantId, usuario.Id, emitido.Jti, emitido.Emision, emitido.Expiracion));
        await repositorio.GuardarAsync(cancellationToken);

        return new ResultadoInicioSesion(emitido.Token, emitido.Expiracion, acceso.CodigoRol, usuario.CambioContrasenaObligatorio);
    }
}
