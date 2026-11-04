using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Cuerpo opcional de API-AUTH-LOGOUT: «voluntario» (predeterminado) o «inactividad».</summary>
public sealed record SolicitudCierreSesion(string? Motivo);

/// <summary>
/// Cierre de sesión (HU-012, RF-011): revoca el jti del token actual para que no pueda
/// reutilizarse (CA-HU-012-02). El cliente lo llama también al cerrar por inactividad
/// (CA-HU-012-01). El motivo guardado es siempre uno de los textos fijos.
/// </summary>
public sealed class CerrarSesion(IRepositorioSesiones repositorio, IRegistroAuditoria auditoria, TimeProvider tiempo)
{
    public const string MotivoVoluntario = "voluntario";
    public const string MotivoInactividad = "inactividad";

    public async Task EjecutarAsync(string? jti, string? motivo, CancellationToken cancellationToken)
    {
        var clave = string.IsNullOrWhiteSpace(motivo) ? MotivoVoluntario : motivo.Trim().ToLowerInvariant();
        var texto = clave switch
        {
            MotivoVoluntario => MotivosRevocacion.CierreDeSesion,
            MotivoInactividad => MotivosRevocacion.Inactividad,
            _ => throw ErrorValidacion.DeCampo("motivo", "El motivo debe ser «voluntario» o «inactividad»."),
        };

        var sesion = string.IsNullOrWhiteSpace(jti) ? null : await repositorio.ObtenerAsync(jti, cancellationToken);
        if (sesion is null)
        {
            throw new ErrorNoAutenticado();
        }

        if (sesion.Revocar(tiempo.GetUtcNow(), texto))
        {
            await repositorio.GuardarAsync(cancellationToken);
            await auditoria.RegistrarAsync(
                sesion.UsuarioId,
                AccionesAuditoria.CierreSesion,
                "sesion",
                sesion.Id.ToString(),
                new Dictionary<string, string> { ["motivo"] = clave },
                cancellationToken);
        }
    }
}
