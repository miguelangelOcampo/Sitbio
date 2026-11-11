using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>
/// Equipo tal como lo devuelve API-EQ y API-EQ-LIST (DOC-06): identificación, ubicación, estado,
/// información regulatoria (RF-030) con el aviso de RN-010 y el plan preventivo activo, si existe.
/// </summary>
public sealed record EquipoDto(
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
    PlanPreventivoDto? Preventivo)
{
    public static EquipoDto Desde(EquipoLeido leido, DateOnly hoy)
    {
        var equipo = leido.Equipo;
        return new EquipoDto(
            equipo.Id,
            equipo.PlacaInventario,
            equipo.NumeroSerie,
            equipo.Marca,
            equipo.Modelo,
            equipo.TipoEquipoId,
            leido.TipoEquipo,
            equipo.UbicacionId,
            leido.Ubicacion,
            equipo.ProveedorId,
            leido.Proveedor,
            leido.EstadoCodigo,
            leido.EstadoNombre,
            equipo.FechaAdquisicion,
            equipo.FechaBaja,
            equipo.TipoAutorizacion,
            equipo.RegistroInvima,
            equipo.VenceRegistro,
            equipo.AvisoDelRegistro(hoy).ToString(),
            equipo.ClaseRiesgo,
            equipo.RequiereCalibracion,
            equipo.PeriodicidadCalibracionMeses,
            equipo.FotoDocumentoId is not null,
            leido.Plan is null ? null : PlanPreventivoDto.Desde(leido.Plan, hoy));
    }
}

/// <summary>Plan preventivo activo del equipo (RF-019); «Vencido» genera alerta (RF-020).</summary>
public sealed record PlanPreventivoDto(Guid Id, int PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly ProximaFecha, bool Vencido)
{
    public static PlanPreventivoDto Desde(PlanPreventivo plan, DateOnly hoy) =>
        new(plan.Id, plan.PeriodicidadDias, plan.UltimaEjecucion, plan.ProximaFecha, plan.EstaVencido(hoy));
}

/// <summary>
/// Datos que AC-02 envía al registrar o editar un equipo. Los campos son anulables para responder
/// 400 por campo cuando faltan; RN-010 los exige al crear y al editar.
/// </summary>
public sealed record DatosEquipo(
    Guid? TipoEquipoId,
    Guid? UbicacionId,
    Guid? ProveedorId,
    string? NumeroSerie,
    string? PlacaInventario,
    string? Marca,
    string? Modelo,
    DateOnly? FechaAdquisicion,
    string? TipoAutorizacion,
    string? RegistroInvima,
    DateOnly? VenceRegistro,
    string? ClaseRiesgo,
    bool? RequiereCalibracion,
    int? PeriodicidadCalibracionMeses,
    DatosPreventivo? Preventivo = null);

/// <summary>
/// Plan preventivo del formulario (CA-HU-002-06). Si falta el próximo, se calcula como
/// último + periodicidad; si no se conoce el último, el próximo es obligatorio y no puede haber pasado.
/// </summary>
public sealed record DatosPreventivo(int PeriodicidadDias, DateOnly? UltimaEjecucion, DateOnly? ProximaFecha, string? Descripcion = null);

/// <summary>Filtros de GET /equipos (API-EQ-LIST). Placa y serie buscan por coincidencia parcial.</summary>
public sealed record FiltroEquipos(
    string? Placa = null,
    string? Serie = null,
    string? Estado = null,
    string? Clase = null,
    bool? Calibracion = null,
    int Pagina = 1,
    int Tamano = 20);

/// <summary>Equipo con los nombres de su tipo, ubicación, proveedor y estado, y su plan activo.</summary>
public sealed record EquipoLeido(
    EquipoBiomedico Equipo,
    string TipoEquipo,
    string Ubicacion,
    string? Proveedor,
    string EstadoCodigo,
    string EstadoNombre,
    PlanPreventivo? Plan);

/// <summary>Elemento de un catálogo con su estado (tipo de equipo o ubicación).</summary>
public sealed record ElementoCatalogo(Guid Id, string Nombre, bool Activo);

/// <summary>Equipos activos (los dados de baja no cuentan) frente al máximo del plan (RN-009).</summary>
public sealed record CapacidadEquipos(string Plan, int Maximo, int Activos)
{
    public bool Llena => Activos >= Maximo;
}

/// <summary>Qué dato choca con otro equipo de la institución (RN-001).</summary>
public sealed record Duplicados(bool Serie, bool Placa);

/// <summary>Cambio de estado que pide AC-02 (PATCH /equipos/{id}/estado; RF-014). El motivo es obligatorio.</summary>
public sealed record DatosCambioEstado(string? Estado, string? Motivo);

/// <summary>Un registro del historial con los nombres de sus estados y de quien hizo el cambio.</summary>
public sealed record CambioEstadoLeido(
    HistorialEstadoEquipo Registro,
    string? EstadoAnteriorCodigo,
    string? EstadoAnteriorNombre,
    string NuevoEstadoCodigo,
    string NuevoEstadoNombre,
    string Usuario);

/// <summary>Fila del historial de estados (GET /equipos/{id}/estados). Sin estado anterior en el alta.</summary>
public sealed record CambioEstadoDto(
    Guid Id,
    DateTimeOffset Fecha,
    string? EstadoAnterior,
    string? EstadoAnteriorNombre,
    string Estado,
    string EstadoNombre,
    string? Motivo,
    string Usuario)
{
    public static CambioEstadoDto Desde(CambioEstadoLeido leido) => new(
        leido.Registro.Id,
        leido.Registro.FechaCambio,
        leido.EstadoAnteriorCodigo,
        leido.EstadoAnteriorNombre,
        leido.NuevoEstadoCodigo,
        leido.NuevoEstadoNombre,
        leido.Registro.Motivo,
        leido.Usuario);
}
