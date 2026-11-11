using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Instituciones;

/// <summary>
/// Institución cliente (tabla <c>tenant</c>), raíz del aislamiento. Su Administrador (AC-01)
/// edita nombre, NIT y anticipación de las alertas (HU-024, RF-024); el plan, sus límites y su
/// vigencia los fija el aprovisionamiento del proveedor (ST-10, RN-009).
/// </summary>
[SuppressMessage("Major Bug", "S3453:Classes should not have only private constructors",
    Justification = "EF Core materializa la entidad con el constructor privado; los tenants los crea el aprovisionamiento (ST-10).")]
public sealed partial class Tenant
{
    public const int LongitudMinimaNombre = 4;
    public const int LongitudMaximaNombre = 150;
    public const int DiasMinimos = 7;
    public const int DiasMaximos = 90;
    public const string FormatoNit = "900.000.000-0";

    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>NIT de la IPS con dígito de verificación (único en el sistema, script 01).</summary>
    public string Nit { get; private set; } = string.Empty;

    /// <summary>Días de anticipación de las alertas de vencimiento (RF-020).</summary>
    public short DiasAnticipacionAlerta { get; private set; }

    public bool EstadoActivo { get; private set; }

    /// <summary>Plan contratado (RN-009): Consultorio, Clínica, Hospital o Red de Salud.</summary>
    public string Plan { get; private set; } = string.Empty;

    /// <summary>Máximo de usuarios de gestión activos del plan (V002; lo fija el aprovisionamiento, ST-10).</summary>
    public int MaxUsuariosGestion { get; private set; }

    /// <summary>Máximo de equipos activos del plan; los dados de baja no cuentan (V002, RN-009).</summary>
    public int MaxEquipos { get; private set; }

    /// <summary>Almacenamiento de documentos del plan, en GB (V002, V003).</summary>
    public int MaxAlmacenamientoGb { get; private set; }

    /// <summary>Fin de la vigencia del plan contratado; null = sin fecha registrada.</summary>
    public DateOnly? VigenteHasta { get; private set; }

    /// <summary>
    /// HU-024: datos editables por el Administrador. Lanza <see cref="ArgumentException"/> con el
    /// nombre del campo (nombre, nit, diasAnticipacionAlerta) sin cambiar nada si alguno no es válido.
    /// </summary>
    public void ActualizarDatos(string? nombre, string? nit, int diasAnticipacionAlerta)
    {
        var nombreValido = Texto.Requerido(nombre, "nombre", LongitudMaximaNombre);
        if (nombreValido.Length < LongitudMinimaNombre)
        {
            throw new ArgumentException($"El nombre de la institución debe tener al menos {LongitudMinimaNombre} caracteres.", "nombre");
        }

        var nitValido = Texto.Requerido(nit, "nit", 20);
        if (!PatronNit().IsMatch(nitValido))
        {
            throw new ArgumentException($"Usa el formato {FormatoNit}.", "nit");
        }

        if (diasAnticipacionAlerta is < DiasMinimos or > DiasMaximos)
        {
            throw new ArgumentException($"Usa un valor entre {DiasMinimos} y {DiasMaximos} días.", "diasAnticipacionAlerta");
        }

        Nombre = nombreValido;
        Nit = nitValido;
        DiasAnticipacionAlerta = (short)diasAnticipacionAlerta;
    }

    [GeneratedRegex(@"^\d{3}\.\d{3}\.\d{3}-\d$")]
    private static partial Regex PatronNit();
}
