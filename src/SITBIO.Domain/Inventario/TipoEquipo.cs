using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Tipo de equipo del catálogo técnico (tabla <c>tipo_equipo</c>, script 01 + V008). Guarda los
/// valores regulatorios que se proponen al registrar un equipo de este tipo (CA-HU-002-04);
/// el equipo puede diferir.
/// </summary>
public sealed class TipoEquipo : EntidadTenant
{
    public const int LongitudMaximaNombre = 80;

    // Constructor para EF Core.
    private TipoEquipo(Guid tenantId)
        : base(tenantId)
    {
    }

    public TipoEquipo(Guid tenantId, string nombre, string? descripcion = null)
        : base(tenantId)
    {
        Nombre = Texto.Requerido(nombre, nameof(nombre), LongitudMaximaNombre);
        Descripcion = Texto.Opcional(descripcion, 200, nameof(descripcion));
        EstadoActivo = true;
        RequiereCalibracion = true;
        PeriodicidadCalibracionMeses = 12;
    }

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public bool EstadoActivo { get; private set; }

    public string? ClaseRiesgoDefecto { get; private set; }

    public bool RequiereCalibracion { get; private set; }

    public short? PeriodicidadCalibracionMeses { get; private set; }

    /// <summary>Valores por defecto (RN-010): la clase no decide la calibración.</summary>
    public void DefinirValoresRegulatorios(string? claseRiesgo, bool requiereCalibracion, int? periodicidadMeses)
    {
        var periodicidad = Calibracion.Periodicidad(requiereCalibracion, periodicidadMeses);
        ClaseRiesgoDefecto = Calibracion.ClaseOpcional(claseRiesgo);
        RequiereCalibracion = requiereCalibracion;
        PeriodicidadCalibracionMeses = periodicidad;
    }
}
