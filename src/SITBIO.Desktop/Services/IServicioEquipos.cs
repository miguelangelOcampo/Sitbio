namespace Sitbio.Desktop.Services;

/// <summary>
/// Inventario (API-EQ-LIST y API-EQ, HU-002): consulta para AC-01 a AC-05 y AC-07; registro y
/// edición para AC-02. El permiso y el tenant los decide la API (RNF-003, RN-008).
/// </summary>
public interface IServicioEquipos
{
    Task<PaginaEquipos> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken);

    Task<Equipo> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    Task<Equipo> CrearAsync(DatosEquipo datos, CancellationToken cancellationToken);

    Task<Equipo> ActualizarAsync(Guid id, DatosEquipo datos, CancellationToken cancellationToken);

    /// <summary>Línea de tiempo del equipo (HU-023): del evento más antiguo al más reciente.</summary>
    Task<LineaTiempo> LineaTiempoAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Evento de GET /equipos/{id}/linea-tiempo; <see cref="Tipo"/> es registro, estado, tiquete, orden, intervencion o calibracion.</summary>
public sealed record EventoLineaTiempo(DateTimeOffset Fecha, string Tipo, string Titulo, string? Detalle, string? Responsable, Guid? ReferenciaId);

/// <summary>Respuesta de GET /equipos/{id}/linea-tiempo; IntervencionesCompletas es false si faltan las de MongoDB.</summary>
public sealed record LineaTiempo(Guid EquipoId, string PlacaInventario, IReadOnlyList<EventoLineaTiempo> Eventos, bool IntervencionesCompletas);

/// <summary>Cuerpo de POST y PUT /equipos (RF-030, CA-HU-002-06).</summary>
public sealed record DatosEquipo(
    Guid? TipoEquipoId,
    Guid? UbicacionId,
    Guid? ProveedorId,
    string NumeroSerie,
    string PlacaInventario,
    string Marca,
    string Modelo,
    DateOnly? FechaAdquisicion,
    string? TipoAutorizacion,
    string RegistroInvima,
    DateOnly? VenceRegistro,
    string? ClaseRiesgo,
    bool? RequiereCalibracion,
    int? PeriodicidadCalibracionMeses,
    DatosPreventivo? Preventivo);

/// <summary>Plan preventivo del formulario: sin próximo, la API lo calcula como último + periodicidad.</summary>
public sealed record DatosPreventivo(int PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly? ProximaFecha);

/// <summary>Filtros de GET /equipos; null = sin filtro. Placa y serie buscan por coincidencia parcial.</summary>
public sealed record FiltroEquipos(
    string? Placa = null,
    string? Serie = null,
    string? Estado = null,
    string? Clase = null,
    bool? Calibracion = null,
    int Pagina = 1,
    int Tamano = 20);

/// <summary>Equipo tal como lo devuelve la API (EquipoDto), con la información regulatoria (RF-030).</summary>
public sealed record Equipo(
    Guid Id,
    string PlacaInventario,
    string NumeroSerie,
    string Marca,
    string Modelo,
    Guid TipoEquipoId,
    string TipoEquipo,
    Guid UbicacionId,
    string Ubicacion,
    Guid? ProveedorId,
    string? Proveedor,
    string Estado,
    string EstadoNombre,
    DateOnly? FechaAdquisicion,
    DateTimeOffset? FechaBaja,
    string? TipoAutorizacion,
    string? RegistroInvima,
    DateOnly? VenceRegistro,
    string AvisoRegistro,
    string? ClaseRiesgo,
    bool RequiereCalibracion,
    int? PeriodicidadCalibracionMeses,
    bool TieneFoto,
    PlanPreventivo? Preventivo);

/// <summary>Plan preventivo activo del equipo; «Vencido» lo calcula la API con la fecha de Colombia.</summary>
public sealed record PlanPreventivo(Guid Id, int PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly ProximaFecha, bool Vencido);

public sealed record PaginaEquipos(IReadOnlyList<Equipo> Items, int Page, int PageSize, int Total);

/// <summary>Valores de <see cref="Equipo.AvisoRegistro"/> (RN-010: aviso, no bloqueo).</summary>
public static class AvisosRegistro
{
    public const string SinDato = "SinDato";
    public const string Vigente = "Vigente";
    public const string PorVencer = "PorVencer";
    public const string Vencido = "Vencido";
}
