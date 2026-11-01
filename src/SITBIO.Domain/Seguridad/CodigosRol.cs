namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Roles de la aplicación (DOC-01 Tabla 3, DOC-03 Tabla 8). Coinciden con <c>rol.codigo</c>
/// (V004); los nombres siguen el ajuste de roles del 01/11/2026 (V006).
/// </summary>
public static class CodigosRol
{
    public const string AdministradorSistema = "AC-01";
    public const string IngenieroBiomedico = "AC-02";
    public const string TecnicoMantenimiento = "AC-03";
    public const string PersonalClinico = "AC-04";

    /// <summary>Gerente de Tecnología Biomédica: responsable gerencial del área biomédica (no es el gerente general de la IPS).</summary>
    public const string GerenteBiomedico = "AC-05";
    public const string DirectivaInstitucional = "AC-06";
    public const string EnteRegulador = "AC-07";

    public static IReadOnlyList<string> Todos { get; } =
    [
        AdministradorSistema,
        IngenieroBiomedico,
        TecnicoMantenimiento,
        PersonalClinico,
        GerenteBiomedico,
        DirectivaInstitucional,
        EnteRegulador,
    ];
}
