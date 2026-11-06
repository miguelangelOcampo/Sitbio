using System.Globalization;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Tono del punto de estado (StatusDot), sin pastillas de color.</summary>
public enum TonoEstado
{
    Exito,
    Advertencia,
    Critico,
    Neutro,
}

/// <summary>Textos de UI-005 y UI-006 para estados, clase de riesgo, autorización y fechas.</summary>
public static class TextosEquipo
{
    public const string SinDato = "—";

    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Clase de riesgo según el Decreto 4725 de 2005 (prototipo: CLASE_DESC).</summary>
    public static readonly IReadOnlyDictionary<string, string> DescripcionClase = new Dictionary<string, string>
    {
        ["I"] = "Riesgo bajo",
        ["IIa"] = "Riesgo moderado",
        ["IIb"] = "Riesgo alto",
        ["III"] = "Riesgo muy alto",
    };

    public static TonoEstado Tono(string codigoEstado) => codigoEstado switch
    {
        "OPERATIVO" => TonoEstado.Exito,
        "EN_MANTENIMIENTO" => TonoEstado.Advertencia,
        "FUERA_DE_SERVICIO" => TonoEstado.Critico,
        _ => TonoEstado.Neutro,
    };

    public static string Fecha(DateOnly? fecha) => fecha?.ToString("dd/MM/yyyy", Cultura) ?? SinDato;

    public static string Clase(string? clase) =>
        clase is not null && DescripcionClase.TryGetValue(clase, out var descripcion) ? $"{clase} · {descripcion}" : SinDato;

    public static string Autorizacion(string? tipo) => tipo switch
    {
        "registro_sanitario" => "Registro sanitario",
        "permiso_comercializacion" => "Permiso de comercialización",
        _ => SinDato,
    };

    /// <summary>Aviso corto bajo el estado en la lista (RN-010); null si está vigente o el equipo está de baja.</summary>
    public static string? AvisoCorto(Equipo equipo) => equipo.FechaBaja is not null ? null : equipo.AvisoRegistro switch
    {
        AvisosRegistro.Vencido => "Registro vencido",
        AvisosRegistro.PorVencer => "Registro por vencer",
        AvisosRegistro.SinDato => "Sin registro",
        _ => null,
    };

    public static string Calibracion(Equipo equipo) =>
        equipo.RequiereCalibracion ? $"Cada {equipo.PeriodicidadCalibracionMeses} {(equipo.PeriodicidadCalibracionMeses == 1 ? "mes" : "meses")}" : "No aplica";

    public static string Periodicidad(int dias) => dias switch
    {
        1 => "Cada día",
        7 => "Semanal",
        30 => "Mensual",
        90 => "Trimestral",
        180 => "Semestral",
        365 => "Anual",
        _ => $"Cada {dias} días",
    };
}
