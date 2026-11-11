namespace Sitbio.Application.Catalogos;

/// <summary>
/// Elemento de un catálogo maestro (API-CAT, DOC-06). En los tipos de equipo lleva los valores
/// regulatorios que se proponen al registrar un equipo (RF-030, CA-HU-002-04); en los proveedores,
/// su NIT y tipo; en los demás catálogos esos campos van nulos. Los datos de contacto del proveedor
/// no se listan.
/// </summary>
public sealed record CatalogoDto(
    Guid Id,
    string Nombre,
    bool Activo,
    string? ClaseRiesgo = null,
    bool? RequiereCalibracion = null,
    int? PeriodicidadCalibracionMeses = null,
    string? Descripcion = null,
    string? Nit = null,
    string? TipoProveedor = null);

/// <summary>Valores de <c>{tipo}</c> en /catalogos/{tipo}.</summary>
public static class TiposCatalogo
{
    public const string TiposEquipo = "tipos-equipo";
    public const string Ubicaciones = "ubicaciones";
    public const string Proveedores = "proveedores";
    public const string TiposFalla = "tipos-falla";

    /// <summary>Los cuatro catálogos de HU-025, en el orden de UI-020.</summary>
    public static IReadOnlyList<string> Todos { get; } = [TiposEquipo, Ubicaciones, Proveedores, TiposFalla];

    /// <summary>Tipo en minúsculas y sin espacios; null si no es un catálogo conocido.</summary>
    public static string? Normalizar(string? tipo)
    {
        var valor = tipo?.Trim().ToLowerInvariant();
        return valor is not null && Todos.Contains(valor) ? valor : null;
    }
}

/// <summary>Códigos de error estables de los catálogos (409).</summary>
public static class ErroresCatalogo
{
    /// <summary>Ya existe un elemento con ese nombre (o un proveedor con ese NIT) en la institución.</summary>
    public const string CodigoDuplicado = "CAT-001";
}
