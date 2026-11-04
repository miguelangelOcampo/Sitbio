namespace Sitbio.Application.Auditoria;

/// <summary>
/// Bitácora de auditoría del tenant del token (RF-013). Es de solo inserción (DOC-06): no
/// hay operaciones para modificar ni borrar eventos.
/// </summary>
public interface IBitacoraAuditoria
{
    Task RegistrarAsync(EventoAuditoria evento, CancellationToken cancellationToken);

    /// <summary>Eventos que cumplen el filtro, del más reciente al más antiguo (CA-HU-010-03).</summary>
    Task<(IReadOnlyList<EventoAuditoria> Items, long Total)> ConsultarAsync(FiltroBitacora filtro, CancellationToken cancellationToken);
}

/// <summary>Filtros de la consulta: usuario, rango de fechas [desde, hasta) y acción; página desde 1.</summary>
public sealed record FiltroBitacora(
    Guid? UsuarioId = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null,
    string? Accion = null,
    int Pagina = 1,
    int Tamano = 50);
