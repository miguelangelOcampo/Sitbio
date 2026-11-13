namespace Sitbio.Application.Mantenimiento;

/// <summary>Origen de la alerta (RF-020): un plan preventivo o la vigencia de una calibración.</summary>
public static class TiposAlerta
{
    public const string Preventivo = "preventivo";
    public const string Calibracion = "calibracion";
}

/// <summary>
/// «proxima»: la fecha cae dentro de los días de anticipación de la institución (CA-HU-020-01);
/// «vencida»: la fecha ya pasó (CA-HU-020-02).
/// </summary>
public static class EstadosAlerta
{
    public const string Proxima = "proxima";
    public const string Vencida = "vencida";
}

/// <summary>
/// Alerta de API-ALERTAS (DOC-06). <see cref="DiasRestantes"/> es negativo cuando ya venció. En un
/// preventivo, <see cref="PlanId"/> y, si ya se generó, la orden de esa fecha con su estado.
/// </summary>
public sealed record AlertaDto(
    string Tipo,
    string Estado,
    DateOnly Fecha,
    int DiasRestantes,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string Descripcion,
    Guid? PlanId = null,
    Guid? OrdenId = null,
    string? EstadoOrden = null);

/// <summary>Respuesta de GET /alertas: las alertas, los días de anticipación usados y las órdenes que se generaron en esta consulta.</summary>
public sealed record AlertasDto(int DiasAnticipacion, IReadOnlyList<AlertaDto> Alertas, int OrdenesGeneradas);

/// <summary>Plan activo que vence hasta la fecha límite, con su equipo y, si existe, la orden de su próxima fecha.</summary>
public sealed record PlanPorVencer(PlanLeido Leido, Guid? OrdenId, string? EstadoOrden);

/// <summary>Equipo que requiere calibración con su última vigencia hasta la fecha límite.</summary>
public sealed record CalibracionPorVencer(Guid EquipoId, string PlacaInventario, string TipoEquipo, string Ubicacion, DateOnly FechaVencimiento);
