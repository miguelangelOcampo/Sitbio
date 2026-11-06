namespace Sitbio.Domain.Inventario;

/// <summary>Clase de riesgo del dispositivo médico según el Decreto 4725 de 2005 (CHECK de la V008).</summary>
public static class ClasesRiesgo
{
    public const string I = "I";
    public const string IIa = "IIa";
    public const string IIb = "IIb";
    public const string III = "III";

    public static IReadOnlyList<string> Todas { get; } = [I, IIa, IIb, III];

    public static bool EsValida(string? clase) => clase is not null && Todas.Contains(clase, StringComparer.Ordinal);
}

/// <summary>Tipo de autorización sanitaria INVIMA (RF-030; CHECK de la V008).</summary>
public static class TiposAutorizacion
{
    public const string RegistroSanitario = "registro_sanitario";
    public const string PermisoComercializacion = "permiso_comercializacion";

    public static IReadOnlyList<string> Todos { get; } = [RegistroSanitario, PermisoComercializacion];

    public static bool EsValido(string? tipo) => tipo is not null && Todos.Contains(tipo, StringComparer.Ordinal);
}

/// <summary>Situación de la autorización INVIMA frente a la fecha de hoy (RN-010: aviso, no bloqueo).</summary>
public enum AvisoRegistro
{
    /// <summary>El equipo aún no tiene la vigencia registrada (equipos anteriores a la V008).</summary>
    SinDato,
    Vigente,

    /// <summary>Faltan <see cref="Calibracion.DiasAvisoRegistro"/> días o menos para el vencimiento.</summary>
    PorVencer,
    Vencido,
}

/// <summary>
/// Reglas de calibración y del aviso de registro (RN-010). La clase de riesgo NO decide la
/// calibración: un tensiómetro (clase I) se calibra y un estetoscopio (clase I) no.
/// </summary>
public static class Calibracion
{
    public const int PeriodicidadMinimaMeses = 1;
    public const int PeriodicidadMaximaMeses = 60;
    public const int DiasAvisoRegistro = 180;

    /// <summary>
    /// Periodicidad que se guarda: 1–60 meses si requiere calibración; null si no la requiere
    /// (la V008 rechaza una periodicidad cuando no hay calibración).
    /// </summary>
    public static short? Periodicidad(bool requiereCalibracion, int? meses)
    {
        if (!requiereCalibracion)
        {
            return null;
        }

        if (meses is not { } valor || valor < PeriodicidadMinimaMeses || valor > PeriodicidadMaximaMeses)
        {
            throw new ArgumentException(
                $"Si el equipo requiere calibración, la periodicidad va de {PeriodicidadMinimaMeses} a {PeriodicidadMaximaMeses} meses.",
                nameof(meses));
        }

        return (short)valor;
    }

    public static string? ClaseOpcional(string? clase)
    {
        if (clase is null)
        {
            return null;
        }

        return ClasesRiesgo.EsValida(clase)
            ? clase
            : throw new ArgumentException("La clase de riesgo debe ser I, IIa, IIb o III.", nameof(clase));
    }

    public static AvisoRegistro Aviso(DateOnly? venceRegistro, DateOnly hoy)
    {
        if (venceRegistro is not { } vence)
        {
            return AvisoRegistro.SinDato;
        }

        if (vence < hoy)
        {
            return AvisoRegistro.Vencido;
        }

        return vence.DayNumber - hoy.DayNumber <= DiasAvisoRegistro ? AvisoRegistro.PorVencer : AvisoRegistro.Vigente;
    }
}
