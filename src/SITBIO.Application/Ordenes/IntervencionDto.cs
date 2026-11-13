using System.Text.Json;

namespace Sitbio.Application.Ordenes;

/// <summary>Estados de salida del equipo en la intervención (enum de <c>intervenciones</c>, script 02).</summary>
public static class EstadosSalidaEquipo
{
    public const string Operativo = "Operativo";
    public const string EnMantenimiento = "En mantenimiento";
    public const string FueraDeServicio = "Fuera de servicio";
    public const string Baja = "Baja";

    public static IReadOnlyList<string> Todos { get; } = [Operativo, EnMantenimiento, FueraDeServicio, Baja];
}

/// <summary>Repuesto usado en la intervención (embebido en el documento).</summary>
public sealed record Repuesto(string Nombre, int Cantidad, decimal? CostoUnitario);

/// <summary>
/// Hoja de servicio de una orden (documento de <c>intervenciones</c> en MongoDB; HU-005, RF-005). Los
/// <see cref="CamposDinamicos"/> guardan valores tipados (long, double, string, bool o DateOnly) según la
/// versión <see cref="VersionCampos"/> de <c>configuracion_campos</c> con la que se validaron.
/// </summary>
public sealed record Intervencion(
    Guid Id,
    Guid OrdenId,
    Guid EquipoId,
    Guid TecnicoId,
    string TipoMantenimiento,
    string Diagnostico,
    string AccionesRealizadas,
    IReadOnlyList<Repuesto> Repuestos,
    decimal? CostoManoObra,
    decimal CostoTotal,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    IReadOnlyDictionary<string, object> CamposDinamicos,
    int? VersionCampos,
    DateTimeOffset FechaIntervencion,
    bool CerradaFirmada);

/// <summary>Intervención guardada con el <c>_id</c> (ObjectId) de su documento.</summary>
public sealed record IntervencionGuardada(string ObjectId, Intervencion Intervencion);

/// <summary>Respuesta de API-INT (IntervencionDto).</summary>
public sealed record IntervencionDto(
    Guid Id,
    Guid OrdenId,
    Guid EquipoId,
    Guid TecnicoId,
    string TipoMantenimiento,
    string Diagnostico,
    string AccionesRealizadas,
    IReadOnlyList<Repuesto> Repuestos,
    decimal? CostoManoObra,
    decimal CostoTotal,
    int? TiempoAtencionMin,
    string? EstadoEquipoSalida,
    IReadOnlyDictionary<string, object> CamposDinamicos,
    int? VersionCampos,
    DateTimeOffset FechaIntervencion,
    bool CerradaFirmada)
{
    public static IntervencionDto Desde(Intervencion i) => new(
        i.Id, i.OrdenId, i.EquipoId, i.TecnicoId, i.TipoMantenimiento, i.Diagnostico, i.AccionesRealizadas, i.Repuestos,
        i.CostoManoObra, i.CostoTotal, i.TiempoAtencionMin, i.EstadoEquipoSalida, i.CamposDinamicos, i.VersionCampos,
        i.FechaIntervencion, i.CerradaFirmada);
}

/// <summary>Cuerpo de PUT /ordenes/{id}/intervencion. <see cref="CamposDinamicos"/> llega como JSON y se valida contra la versión activa.</summary>
public sealed record DatosIntervencion(
    string? Diagnostico,
    string? AccionesRealizadas,
    IReadOnlyList<DatosRepuesto?>? Repuestos = null,
    decimal? CostoManoObra = null,
    int? TiempoAtencionMin = null,
    string? EstadoEquipoSalida = null,
    IReadOnlyDictionary<string, JsonElement>? CamposDinamicos = null);

public sealed record DatosRepuesto(string? Nombre, int? Cantidad, decimal? CostoUnitario);

/// <summary>Cuerpo de PATCH /ordenes/{id}/estado.</summary>
public sealed record DatosEstadoOrden(string? Accion);

/// <summary>
/// Intervenciones en MongoDB del tenant del token (RN-008): el repositorio filtra y asigna el tenant.
/// Si MongoDB no responde lanza <c>ErrorServicioNoDisponible</c> (503) y nada cambia en PostgreSQL (RT-002).
/// </summary>
public interface IRepositorioIntervenciones
{
    /// <summary>Intervención de la orden; null si aún no se registró.</summary>
    Task<IntervencionGuardada?> ObtenerAsync(Guid ordenId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserta el documento (sin <paramref name="objectIdExistente"/>) o lo reemplaza si no está firmado
    /// (RN-004). Devuelve el ObjectId del documento.
    /// </summary>
    Task<string> GuardarAsync(Intervencion intervencion, string? objectIdExistente, CancellationToken cancellationToken);
}
