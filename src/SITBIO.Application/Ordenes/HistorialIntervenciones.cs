using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Mantenimiento;

namespace Sitbio.Application.Ordenes;

/// <summary>
/// Filtros de GET /intervenciones (HU-005, historial de intervenciones). <see cref="Equipo"/> busca por placa,
/// serie, marca, modelo o nombre del tipo; <see cref="Desde"/> y <see cref="Hasta"/> son días de Colombia, ambos
/// incluidos. null = sin filtro.
/// </summary>
public sealed record FiltroHistorialIntervenciones(
    string? Equipo = null,
    Guid? TipoEquipoId = null,
    Guid? TecnicoId = null,
    string? TipoMantenimiento = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Pagina = 1,
    int Tamano = 20);

/// <summary>Fila del historial: la intervención con el equipo, el técnico y el tiquete de origen de su orden.</summary>
public sealed record IntervencionHistorialDto(
    Guid Id,
    Guid OrdenId,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    Guid TecnicoId,
    string Tecnico,
    string TipoMantenimiento,
    string Diagnostico,
    decimal CostoTotal,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    bool CerradaFirmada,
    DateTimeOffset FechaIntervencion,
    string? CodigoTiquete);

/// <summary>Totales de todo el filtro (no solo de la página): para las tarjetas de la pantalla.</summary>
public sealed record ResumenHistorial(int Total, int Correctivas, int Preventivas, decimal CostoTotal, double? TiempoPromedioMin);

/// <summary>Página del historial, de la intervención más reciente a la más antigua.</summary>
public sealed record PaginaHistorialIntervenciones(
    IReadOnlyList<IntervencionHistorialDto> Items, int Page, int PageSize, int Total, ResumenHistorial Resumen);

/// <summary>
/// Criterios para MongoDB. <see cref="EquipoIds"/> null = cualquier equipo. <see cref="Hasta"/> es exclusivo.
/// </summary>
public sealed record CriteriosIntervenciones(
    IReadOnlyCollection<Guid>? EquipoIds,
    Guid? TecnicoId,
    string? TipoMantenimiento,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

/// <summary>Página de intervenciones de MongoDB con los totales de todo el filtro.</summary>
public sealed record ResultadoIntervenciones(
    IReadOnlyList<Intervencion> Items,
    int Total,
    int Correctivas,
    decimal CostoTotal,
    double? TiempoPromedioMin);

/// <summary>Datos de PostgreSQL que completan las filas (equipo, técnico y tiquete de la orden).</summary>
public sealed record EquipoDelHistorial(string PlacaInventario, string TipoEquipo);

/// <summary>Lado PostgreSQL del historial; el filtro global lo limita al tenant del token (RN-008).</summary>
public interface IRepositorioHistorialIntervenciones
{
    /// <summary>Ids de los equipos que cumplen el texto y el tipo de equipo.</summary>
    Task<IReadOnlyList<Guid>> EquiposAsync(string? texto, Guid? tipoEquipoId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, EquipoDelHistorial>> DatosEquiposAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Nombre completo de los usuarios pedidos.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Código del tiquete de origen de las órdenes correctivas pedidas.</summary>
    Task<IReadOnlyDictionary<Guid, string>> TiquetesDeOrdenesAsync(IReadOnlyCollection<Guid> ordenIds, CancellationToken cancellationToken);
}

/// <summary>
/// Historial de intervenciones de la institución (HU-005): consulta paginada sobre la colección
/// <c>intervenciones</c> de MongoDB, con los nombres de PostgreSQL. Las dos bases filtran por el tenant del token
/// (RN-008); los roles los exige la API.
/// </summary>
public sealed class ConsultaHistorialIntervenciones(IRepositorioIntervenciones intervenciones, IRepositorioHistorialIntervenciones datos)
{
    public const int TamanoMaximo = 100;

    public async Task<PaginaHistorialIntervenciones> ConsultarAsync(FiltroHistorialIntervenciones filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var tipo = Validar(filtro);
        var texto = string.IsNullOrWhiteSpace(filtro.Equipo) ? null : filtro.Equipo.Trim();

        IReadOnlyCollection<Guid>? equipoIds = null;
        if (texto is not null || filtro.TipoEquipoId is not null)
        {
            equipoIds = await datos.EquiposAsync(texto, filtro.TipoEquipoId, cancellationToken);
            if (equipoIds.Count == 0)
            {
                return new PaginaHistorialIntervenciones([], filtro.Pagina, filtro.Tamano, 0, new ResumenHistorial(0, 0, 0, 0, null));
            }
        }

        var criterios = new CriteriosIntervenciones(
            equipoIds,
            filtro.TecnicoId,
            tipo,
            filtro.Desde is { } desde ? new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase) : null,
            filtro.Hasta is { } hasta ? new DateTimeOffset(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase) : null);
        var resultado = await intervenciones.ConsultarAsync(criterios, (filtro.Pagina - 1) * filtro.Tamano, filtro.Tamano, cancellationToken);

        var equipos = await datos.DatosEquiposAsync(resultado.Items.Select(i => i.EquipoId).Distinct().ToList(), cancellationToken);
        var tecnicos = await datos.NombresAsync(resultado.Items.Select(i => i.TecnicoId).Distinct().ToList(), cancellationToken);
        var tiquetes = await datos.TiquetesDeOrdenesAsync(resultado.Items.Select(i => i.OrdenId).Distinct().ToList(), cancellationToken);

        var filas = resultado.Items.Select(i =>
        {
            var equipo = equipos.GetValueOrDefault(i.EquipoId);
            return new IntervencionHistorialDto(
                i.Id,
                i.OrdenId,
                i.EquipoId,
                equipo?.PlacaInventario ?? string.Empty,
                equipo?.TipoEquipo ?? string.Empty,
                i.TecnicoId,
                tecnicos.GetValueOrDefault(i.TecnicoId) ?? string.Empty,
                i.TipoMantenimiento,
                i.Diagnostico,
                i.CostoTotal,
                i.TiempoAtencionMin,
                i.EstadoEquipoSalida,
                i.CerradaFirmada,
                i.FechaIntervencion,
                tiquetes.GetValueOrDefault(i.OrdenId));
        }).ToList();

        var resumen = new ResumenHistorial(
            resultado.Total,
            resultado.Correctivas,
            resultado.Total - resultado.Correctivas,
            resultado.CostoTotal,
            resultado.TiempoPromedioMin);
        return new PaginaHistorialIntervenciones(filas, filtro.Pagina, filtro.Tamano, resultado.Total, resumen);
    }

    /// <summary>Devuelve el tipo de mantenimiento normalizado (o null) si los filtros son válidos.</summary>
    private static string? Validar(FiltroHistorialIntervenciones filtro)
    {
        var errores = new Dictionary<string, string[]>();
        if (filtro.Pagina < 1)
        {
            errores["page"] = ["La página debe ser 1 o mayor."];
        }

        if (filtro.Tamano is < 1 or > TamanoMaximo)
        {
            errores["pageSize"] = [$"El tamaño de página debe estar entre 1 y {TamanoMaximo}."];
        }

        var tipo = filtro.TipoMantenimiento?.Trim().ToLowerInvariant();
        if (tipo is { Length: 0 })
        {
            tipo = null;
        }

        if (tipo is not (null or TiposMantenimiento.Preventivo or TiposMantenimiento.Correctivo))
        {
            errores["tipoMantenimiento"] = ["El tipo de mantenimiento debe ser «preventivo» o «correctivo»."];
        }

        if (filtro is { Desde: { } desde, Hasta: { } hasta } && hasta < desde)
        {
            errores["hasta"] = ["La fecha final no puede ser anterior a la inicial."];
        }

        return errores.Count > 0 ? throw new ErrorValidacion(errores) : tipo;
    }
}
