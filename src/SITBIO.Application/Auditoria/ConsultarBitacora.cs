using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Auditoria;

/// <summary>Evento de la bitácora con el nombre del usuario para mostrarlo en UI-024.</summary>
public sealed record EventoBitacoraDto(
    Guid Id,
    DateTimeOffset FechaHora,
    Guid UsuarioId,
    string? Usuario,
    string Accion,
    string Entidad,
    string? EntidadId,
    string? IpOrigen,
    IReadOnlyDictionary<string, string>? Detalle);

/// <summary>Nombres de los usuarios del tenant del token (sin otros datos personales).</summary>
public interface IDirectorioUsuarios
{
    Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// Consulta de la bitácora por usuario, fecha y acción (CA-HU-010-03). El permiso (AC-01 o
/// AC-07) lo exige la API; el tenant sale del token, así nunca se ven eventos de otra
/// institución (CA-HU-010-02).
/// </summary>
public sealed class ConsultarBitacora(IBitacoraAuditoria bitacora, IDirectorioUsuarios directorio)
{
    public async Task<Pagina<EventoBitacoraDto>> EjecutarAsync(FiltroBitacora filtro, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, string[]>();
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<EventoBitacoraDto>.TamanoMaximo)
        {
            errores["pageSize"] = [$"La página empieza en 1 y el tamaño va de 1 a {Pagina<EventoBitacoraDto>.TamanoMaximo}."];
        }

        if (filtro.Desde is { } desde && filtro.Hasta is { } hasta && desde >= hasta)
        {
            errores["hasta"] = ["La fecha final debe ser posterior a la inicial."];
        }

        if (!string.IsNullOrWhiteSpace(filtro.Accion) && !AccionesAuditoria.Todas.Contains(filtro.Accion))
        {
            errores["accion"] = ["La acción no existe en el catálogo de la bitácora."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var (eventos, total) = await bitacora.ConsultarAsync(filtro, cancellationToken);
        var nombres = await directorio.NombresAsync(eventos.Select(e => e.UsuarioId).Distinct().ToList(), cancellationToken);

        var items = eventos
            .Select(e => new EventoBitacoraDto(
                e.Id, e.FechaHora, e.UsuarioId, nombres.GetValueOrDefault(e.UsuarioId), e.Accion, e.Entidad, e.EntidadId, e.IpOrigen, e.Detalle))
            .ToList();
        return new Pagina<EventoBitacoraDto>(items, filtro.Pagina, filtro.Tamano, (int)Math.Min(total, int.MaxValue));
    }
}
