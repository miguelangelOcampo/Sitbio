using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Calibración de un equipo (tabla <c>calibracion</c>, script 01; HU-022, RF-022). Solo se registra para
/// equipos que requieren calibración (RN-010). El vencimiento es la fecha de la calibración más la
/// periodicidad del equipo, salvo que el certificado indique otro; nunca es anterior a la fecha
/// (CHECK <c>ck_calibracion_vigencia</c>). El Técnico la registra y la corrige mientras el Ingeniero no la
/// apruebe; aprobada, ya no cambia. La vigencia del equipo es el vencimiento más lejano de sus
/// calibraciones (RN-007).
/// </summary>
public sealed class RegistroCalibracion : EntidadTenant
{
    public const string Conforme = "conforme";
    public const string NoConforme = "no conforme";

    public const int LongitudMaximaPatron = 120;
    public const int LongitudMaximaCertificado = 80;

    /// <summary>
    /// Resultados admitidos. El script 01 también nombra «conforme con observaciones», pero la columna es
    /// VARCHAR(15) y ese valor no cabe; queda pendiente de una migración.
    /// </summary>
    public static IReadOnlyList<string> Resultados { get; } = [Conforme, NoConforme];

    // Constructor para EF Core.
    private RegistroCalibracion(Guid tenantId)
        : base(tenantId)
    {
    }

    public RegistroCalibracion(
        Guid tenantId,
        Guid equipoId,
        Guid registradoPorId,
        DateOnly fechaCalibracion,
        string patronReferencia,
        string resultado,
        DateOnly fechaVencimiento,
        string? numeroCertificado = null,
        Guid? proveedorId = null)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("La calibración debe ser de un equipo.", nameof(equipoId));
        RegistradoPorId = registradoPorId != Guid.Empty ? registradoPorId : throw new ArgumentException("La calibración debe tener quién la registró.", nameof(registradoPorId));
        Asignar(fechaCalibracion, patronReferencia, resultado, fechaVencimiento, numeroCertificado, proveedorId);
    }

    public Guid EquipoId { get; private set; }

    public Guid? ProveedorId { get; private set; }

    public Guid? OrdenId { get; private set; }

    public Guid RegistradoPorId { get; private set; }

    public Guid? AprobadoPorId { get; private set; }

    public DateOnly FechaCalibracion { get; private set; }

    public string PatronReferencia { get; private set; } = string.Empty;

    public string Resultado { get; private set; } = Conforme;

    public string? NumeroCertificado { get; private set; }

    public DateOnly FechaVencimiento { get; private set; }

    public bool Aprobada => AprobadoPorId is not null;

    /// <summary>Fecha de la calibración + la periodicidad del equipo en meses (RN-010).</summary>
    public static DateOnly VencimientoPorPeriodicidad(DateOnly fechaCalibracion, int periodicidadMeses) =>
        periodicidadMeses is >= Calibracion.PeriodicidadMinimaMeses and <= Calibracion.PeriodicidadMaximaMeses
            ? fechaCalibracion.AddMonths(periodicidadMeses)
            : throw new ArgumentException(
                $"La periodicidad de calibración va de {Calibracion.PeriodicidadMinimaMeses} a {Calibracion.PeriodicidadMaximaMeses} meses.",
                nameof(periodicidadMeses));

    /// <summary>Vigente hasta el día de su vencimiento inclusive.</summary>
    public bool EstaVigente(DateOnly hoy) => FechaVencimiento >= hoy;

    /// <summary>Corrige el registro mientras no esté aprobado.</summary>
    public void Corregir(
        DateOnly fechaCalibracion,
        string patronReferencia,
        string resultado,
        DateOnly fechaVencimiento,
        string? numeroCertificado,
        Guid? proveedorId)
    {
        ExigirSinAprobar();
        Asignar(fechaCalibracion, patronReferencia, resultado, fechaVencimiento, numeroCertificado, proveedorId);
    }

    /// <summary>El Ingeniero aprueba el registro; desde entonces no cambia.</summary>
    public void Aprobar(Guid usuarioId)
    {
        ExigirSinAprobar();
        AprobadoPorId = usuarioId != Guid.Empty ? usuarioId : throw new ArgumentException("La aprobación debe tener quién aprueba.", nameof(usuarioId));
    }

    private void Asignar(
        DateOnly fechaCalibracion,
        string patronReferencia,
        string resultado,
        DateOnly fechaVencimiento,
        string? numeroCertificado,
        Guid? proveedorId)
    {
        if (!Resultados.Contains(resultado))
        {
            throw new ArgumentException("El resultado de la calibración debe ser conforme o no conforme.", nameof(resultado));
        }

        if (fechaVencimiento < fechaCalibracion)
        {
            throw new ArgumentException("El vencimiento no puede ser anterior a la fecha de la calibración.", nameof(fechaVencimiento));
        }

        FechaCalibracion = fechaCalibracion;
        PatronReferencia = Texto.Requerido(patronReferencia, nameof(patronReferencia), LongitudMaximaPatron);
        Resultado = resultado;
        FechaVencimiento = fechaVencimiento;
        NumeroCertificado = Texto.Opcional(numeroCertificado, LongitudMaximaCertificado, nameof(numeroCertificado));
        ProveedorId = proveedorId == Guid.Empty ? null : proveedorId;
    }

    private void ExigirSinAprobar()
    {
        if (Aprobada)
        {
            throw new InvalidOperationException("La calibración ya está aprobada; no se puede modificar.");
        }
    }
}
