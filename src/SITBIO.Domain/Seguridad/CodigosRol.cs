namespace Sitbio.Domain.Seguridad;

/// <summary>Roles de la aplicación (DOC-01 Tabla 3, DOC-03 Tabla 8).</summary>
public static class CodigosRol
{
    public const string AdministradorSistema = "AC-01";
    public const string IngenieroBiomedico = "AC-02";
    public const string TecnicoMantenimiento = "AC-03";
    public const string PersonalClinico = "AC-04";
    public const string DirectorBiomedico = "AC-05";
    public const string DirectivaInstitucional = "AC-06";
    public const string EnteRegulador = "AC-07";

    public static IReadOnlyList<string> Todos { get; } =
    [
        AdministradorSistema,
        IngenieroBiomedico,
        TecnicoMantenimiento,
        PersonalClinico,
        DirectorBiomedico,
        DirectivaInstitucional,
        EnteRegulador,
    ];
}
