using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Mantenimiento;

/// <summary>
/// Calibración de API-CAL (DOC-06) con los datos del equipo, el laboratorio y los responsables.
/// <see cref="Vigente"/> si el vencimiento es hoy o después.
/// </summary>
public sealed record CalibracionDto(
    Guid Id,
    Guid EquipoId,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    Guid? ProveedorId,
    string? Proveedor,
    DateOnly FechaCalibracion,
    string PatronReferencia,
    string Resultado,
    string? NumeroCertificado,
    DateOnly FechaVencimiento,
    bool Vigente,
    Guid RegistradoPorId,
    string RegistradoPor,
    Guid? AprobadoPorId,
    string? AprobadoPor,
    bool Aprobada)
{
    public static CalibracionDto Desde(CalibracionLeida leida, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(leida);
        var c = leida.Calibracion;
        return new CalibracionDto(
            c.Id,
            c.EquipoId,
            leida.PlacaInventario,
            leida.TipoEquipo,
            leida.Ubicacion,
            c.ProveedorId,
            leida.Proveedor,
            c.FechaCalibracion,
            c.PatronReferencia,
            c.Resultado,
            c.NumeroCertificado,
            c.FechaVencimiento,
            c.EstaVigente(hoy),
            c.RegistradoPorId,
            leida.RegistradoPor,
            c.AprobadoPorId,
            leida.AprobadoPor,
            c.Aprobada);
    }
}

/// <summary>
/// Cuerpo de POST /calibraciones. Sin resultado, «conforme»; sin vencimiento, la fecha + la periodicidad
/// de calibración del equipo (RN-010).
/// </summary>
public sealed record DatosCalibracion(
    Guid? EquipoId,
    DateOnly? FechaCalibracion,
    string? PatronReferencia,
    string? Resultado,
    string? NumeroCertificado,
    DateOnly? FechaVencimiento,
    Guid? ProveedorId);

/// <summary>
/// Cuerpo de PATCH /calibraciones/{id}: lo que no se envía no cambia. Si cambia la fecha y no se indica el
/// vencimiento, este se recalcula con la periodicidad del equipo.
/// </summary>
public sealed record DatosCorreccionCalibracion(
    DateOnly? FechaCalibracion,
    string? PatronReferencia,
    string? Resultado,
    string? NumeroCertificado,
    DateOnly? FechaVencimiento,
    Guid? ProveedorId);

/// <summary>Filtros de GET /calibraciones.</summary>
public sealed record FiltroCalibraciones(Guid? EquipoId = null, int Pagina = 1, int Tamano = 20);

/// <summary>Calibración con la placa, el tipo y la ubicación del equipo, el laboratorio y los nombres de quién registró y aprobó.</summary>
public sealed record CalibracionLeida(
    RegistroCalibracion Calibracion,
    string PlacaInventario,
    string TipoEquipo,
    string Ubicacion,
    string? Proveedor,
    string RegistradoPor,
    string? AprobadoPor);

/// <summary>Equipo que se calibra: su tenant lo limita la persistencia (RN-008).</summary>
public sealed record EquipoDeCalibracion(Guid Id, string PlacaInventario, bool DadoDeBaja, bool RequiereCalibracion, short? PeriodicidadMeses);
