namespace Sitbio.Application.Catalogos;

/// <summary>
/// Elemento de un catálogo maestro (API-CAT, DOC-06). En los tipos de equipo lleva los valores
/// regulatorios que se proponen al registrar un equipo (RF-030, CA-HU-002-04); en los demás
/// catálogos esos campos van nulos.
/// </summary>
public sealed record CatalogoDto(
    Guid Id,
    string Nombre,
    bool Activo,
    string? ClaseRiesgo = null,
    bool? RequiereCalibracion = null,
    int? PeriodicidadCalibracionMeses = null);

/// <summary>Valores de <c>{tipo}</c> en /catalogos/{tipo}.</summary>
public static class TiposCatalogo
{
    public const string TiposEquipo = "tipos-equipo";
    public const string Ubicaciones = "ubicaciones";
    public const string Proveedores = "proveedores";
}
