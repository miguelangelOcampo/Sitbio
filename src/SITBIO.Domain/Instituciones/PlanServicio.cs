namespace Sitbio.Domain.Instituciones;

/// <summary>
/// Un plan del servicio (DOC-01 Tabla 4, RN-009): todos tienen las mismas funciones y solo cambia la
/// capacidad. Los límites nulos son «a la medida» (Red de Salud) y el valor nulo es «Cotización».
/// </summary>
public sealed record PlanServicio(
    string Nombre,
    int? MaxUsuariosGestion,
    int? MaxEquipos,
    int? MaxAlmacenamientoGb,
    decimal? ValorMensualCop)
{
    public const string Consultorio = "Consultorio";
    public const string Clinica = "Clínica";
    public const string Hospital = "Hospital";
    public const string RedDeSalud = "Red de Salud";

    /// <summary>Planes vigentes, del más pequeño al más grande (mismos valores que V002 y V003).</summary>
    public static IReadOnlyList<PlanServicio> Vigentes { get; } =
    [
        new(Consultorio, 3, 300, 5, 190_000m),
        new(Clinica, 7, 1_000, 25, 380_000m),
        new(Hospital, 14, 3_000, 100, 800_000m),
        new(RedDeSalud, null, null, null, null),
    ];

    /// <summary>El plan con ese nombre exacto (como lo acepta el CHECK de la base) o null.</summary>
    public static PlanServicio? Buscar(string? nombre) => Vigentes.FirstOrDefault(p => p.Nombre == nombre);
}
