using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Elemento de un catálogo maestro (HU-025, RF-025): no se borra, se inactiva para que los
/// registros anteriores conserven su valor (CA-HU-025-02). Un elemento inactivo deja de aparecer
/// como opción en los formularios nuevos.
/// </summary>
public interface IElementoCatalogo
{
    Guid Id { get; }

    string Nombre { get; }

    bool EstadoActivo { get; }

    void Activar();

    void Inactivar();
}

/// <summary>
/// Ubicación (área o servicio) de la institución (tabla <c>ubicacion</c>, script 01). La gestionan
/// AC-02 y también AC-01, que usa su propia nomenclatura de TI (CA-HU-025-03).
/// </summary>
public sealed class Ubicacion : EntidadTenant, IElementoCatalogo
{
    public const int LongitudMaximaNombre = 120;
    public const int LongitudMaximaDescripcion = 200;

    // Constructor para EF Core.
    private Ubicacion(Guid tenantId)
        : base(tenantId)
    {
    }

    public Ubicacion(Guid tenantId, string nombre, string? descripcion = null)
        : base(tenantId)
    {
        Actualizar(nombre, descripcion);
        EstadoActivo = true;
    }

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public bool EstadoActivo { get; private set; }

    public void Actualizar(string nombre, string? descripcion)
    {
        Nombre = Texto.Requerido(nombre, nameof(nombre), LongitudMaximaNombre);
        Descripcion = Texto.Opcional(descripcion, LongitudMaximaDescripcion, nameof(descripcion));
    }

    public void Activar() => EstadoActivo = true;

    public void Inactivar() => EstadoActivo = false;
}

/// <summary>
/// Proveedor de adquisición, servicio técnico o calibración (tabla <c>proveedor</c>, script 01).
/// NIT único por institución. Los datos de contacto son opcionales y no van a la bitácora ni a los
/// registros (sin datos personales en los logs).
/// </summary>
public sealed class Proveedor : EntidadTenant, IElementoCatalogo
{
    public const int LongitudMaximaNombre = 150;
    public const int LongitudMaximaNit = 20;
    public const int LongitudMaximaTipo = 50;
    public const int LongitudMaximaContacto = 100;
    public const int LongitudMaximaCorreo = 100;
    public const int LongitudMaximaTelefono = 30;

    // Constructor para EF Core.
    private Proveedor(Guid tenantId)
        : base(tenantId)
    {
    }

    public Proveedor(Guid tenantId, string nombre, string nit, string tipoProveedor, ContactoProveedor? contacto = null)
        : base(tenantId)
    {
        Actualizar(nombre, nit, tipoProveedor, contacto);
        EstadoActivo = true;
    }

    public string Nombre { get; private set; } = string.Empty;

    public string Nit { get; private set; } = string.Empty;

    /// <summary>Distribuidor, Servicio técnico, Laboratorio de calibración, ...</summary>
    public string TipoProveedor { get; private set; } = string.Empty;

    public string? NombreContacto { get; private set; }

    public string? Correo { get; private set; }

    public string? Telefono { get; private set; }

    public bool EstadoActivo { get; private set; }

    public void Actualizar(string nombre, string nit, string tipoProveedor, ContactoProveedor? contacto)
    {
        Nombre = Texto.Requerido(nombre, nameof(nombre), LongitudMaximaNombre);
        Nit = Texto.Requerido(nit, nameof(nit), LongitudMaximaNit);
        TipoProveedor = Texto.Requerido(tipoProveedor, nameof(tipoProveedor), LongitudMaximaTipo);
        NombreContacto = Texto.Opcional(contacto?.Nombre, LongitudMaximaContacto, "nombreContacto");
        Correo = CorreoOpcional(contacto?.Correo);
        Telefono = Texto.Opcional(contacto?.Telefono, LongitudMaximaTelefono, "telefono");
    }

    public void Activar() => EstadoActivo = true;

    public void Inactivar() => EstadoActivo = false;

    private static string? CorreoOpcional(string? correo)
    {
        var valor = Texto.Opcional(correo, LongitudMaximaCorreo, "correo");
        if (valor is null)
        {
            return null;
        }

        var arroba = valor.IndexOf('@', StringComparison.Ordinal);
        return arroba > 0 && arroba < valor.Length - 1 && !valor.Contains(' ', StringComparison.Ordinal)
            ? valor.ToLowerInvariant()
            : throw new ArgumentException("Escribe un correo válido, por ejemplo soporte@proveedor.com.", nameof(correo));
    }
}

/// <summary>Contacto opcional del proveedor.</summary>
public sealed record ContactoProveedor(string? Nombre, string? Correo, string? Telefono);

/// <summary>
/// Tipo de falla que se elige al reportar una falla (tabla <c>tipo_falla</c>, V009; HU-003). El
/// tiquete guarda el nombre como texto, así que inactivar o renombrar no cambia los tiquetes
/// anteriores (CA-HU-025-02).
/// </summary>
public sealed class TipoFalla : EntidadTenant, IElementoCatalogo
{
    /// <summary>Mismo largo que <c>tiquete.tipo_falla</c>.</summary>
    public const int LongitudMaximaNombre = 60;

    // Constructor para EF Core.
    private TipoFalla(Guid tenantId)
        : base(tenantId)
    {
    }

    public TipoFalla(Guid tenantId, string nombre)
        : base(tenantId)
    {
        Renombrar(nombre);
        EstadoActivo = true;
    }

    public string Nombre { get; private set; } = string.Empty;

    public bool EstadoActivo { get; private set; }

    public void Renombrar(string nombre) => Nombre = Texto.Requerido(nombre, nameof(nombre), LongitudMaximaNombre);

    public void Activar() => EstadoActivo = true;

    public void Inactivar() => EstadoActivo = false;
}
