using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitbio.Application.Auditoria;

namespace Sitbio.Infrastructure.Auditoria;

/// <summary>
/// Registro tolerante a fallos (HU-010): un problema de MongoDB, incluso la falta de su
/// configuración, no debe bloquear un inicio de sesión ni cambiar la respuesta de un 403. Por
/// eso la bitácora se obtiene dentro del try. El log técnico solo anota la acción y el tipo
/// de error, sin datos personales.
/// </summary>
public sealed class RegistroAuditoria(
    IServiceProvider servicios,
    IOrigenSolicitud origen,
    TimeProvider tiempo,
    ILogger<RegistroAuditoria> logger) : IRegistroAuditoria
{
    public async Task RegistrarAsync(
        Guid usuarioId,
        string accion,
        string entidad,
        string? entidadId = null,
        IReadOnlyDictionary<string, string>? detalle = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var evento = EventoAuditoria.Nuevo(usuarioId, accion, entidad, tiempo.GetUtcNow(), entidadId, origen.IpOrigen, detalle);
            await servicios.GetRequiredService<IBitacoraAuditoria>().RegistrarAsync(evento, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("No se pudo registrar {Accion} sobre {Entidad} en la bitácora ({Tipo}).", accion, entidad, error.GetType().Name);
        }
    }
}
