using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Catalogos;

/// <summary>
/// Elemento de un catálogo maestro (API-CAT, DOC-06). En los tipos de equipo lleva los valores
/// regulatorios que se proponen al registrar un equipo (RF-030, CA-HU-002-04); en los proveedores,
/// su NIT y tipo; en los demás catálogos esos campos van nulos. Los datos de contacto del proveedor
/// no se listan: solo vienen en el detalle (GET /catalogos/proveedores/{id}).
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
    string? TipoProveedor = null,
    string? NombreContacto = null,
    string? Correo = null,
    string? Telefono = null)
{
    /// <summary>Detalle de un elemento, con el contacto si es un proveedor.</summary>
    public static CatalogoDto Desde(IElementoCatalogo elemento) => elemento switch
    {
        TipoEquipo t => new(t.Id, t.Nombre, t.EstadoActivo, t.ClaseRiesgoDefecto, t.RequiereCalibracion, t.PeriodicidadCalibracionMeses, t.Descripcion),
        Ubicacion u => new(u.Id, u.Nombre, u.EstadoActivo, Descripcion: u.Descripcion),
        Proveedor p => new(
            p.Id, p.Nombre, p.EstadoActivo, Nit: p.Nit, TipoProveedor: p.TipoProveedor, NombreContacto: p.NombreContacto, Correo: p.Correo, Telefono: p.Telefono),
        _ => new(elemento.Id, elemento.Nombre, elemento.EstadoActivo),
    };
}

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
