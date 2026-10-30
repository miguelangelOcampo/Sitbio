namespace Sitbio.Desktop.ViewModels;

/// <summary>Planes del servicio vigentes (DOC-01 Tabla 4, RN-009): mismas funciones, distinta capacidad.</summary>
public sealed record PlanServicio(string Nombre, string Capacidad, string Valor, string Periodo)
{
    public static IReadOnlyList<PlanServicio> Vigentes { get; } =
    [
        new("Consultorio", "3 usuarios de gestión · 300 equipos · 5 GB", "$190.000", "COP/mes"),
        new("Clínica", "7 usuarios de gestión · 1.000 equipos · 25 GB", "$380.000", "COP/mes"),
        new("Hospital", "14 usuarios de gestión · 3.000 equipos · 100 GB", "$800.000", "COP/mes"),
        new("Red de Salud", "Capacidad a la medida", "Cotización", string.Empty),
    ];
}
