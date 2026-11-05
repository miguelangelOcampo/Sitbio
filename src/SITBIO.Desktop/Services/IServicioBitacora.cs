namespace Sitbio.Desktop.Services;

/// <summary>
/// Consulta de la bitácora de auditoría (API-AUD, HU-010) para AC-01 y AC-07. Solo lectura:
/// la bitácora no se modifica desde el cliente. El permiso y el tenant los decide la API.
/// </summary>
public interface IServicioBitacora
{
    Task<PaginaBitacora> ConsultarAsync(FiltroConsultaBitacora filtro, CancellationToken cancellationToken);
}

/// <summary>Filtros de la consulta; null = sin filtro. <c>Hasta</c> es exclusivo.</summary>
public sealed record FiltroConsultaBitacora(
    Guid? UsuarioId = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null,
    string? Accion = null,
    int Pagina = 1,
    int Tamano = 50);

public sealed record EventoBitacora(
    Guid Id,
    DateTimeOffset FechaHora,
    Guid UsuarioId,
    string? Usuario,
    string Accion,
    string Entidad,
    string? EntidadId,
    string? IpOrigen,
    IReadOnlyDictionary<string, string>? Detalle);

public sealed record PaginaBitacora(IReadOnlyList<EventoBitacora> Items, int Page, int PageSize, int Total);
