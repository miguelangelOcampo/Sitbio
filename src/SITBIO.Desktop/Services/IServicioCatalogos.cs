using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Catálogos maestros (API-CAT, HU-025): consulta, alta y edición; los valores se inactivan, no se borran.</summary>
public interface IServicioCatalogos
{
    /// <summary>GET /catalogos/{tipo}: todos los elementos, activos e inactivos.</summary>
    Task<IReadOnlyList<ElementoCatalogo>> ListarAsync(string tipo, CancellationToken cancellationToken);

    /// <summary>GET /catalogos/{tipo}/{id}: detalle completo (en proveedores, con el contacto).</summary>
    Task<ElementoCatalogo> ObtenerAsync(string tipo, Guid id, CancellationToken cancellationToken);

    /// <summary>POST /catalogos/{tipo}; 409 CAT-001 si el valor ya existe.</summary>
    Task<ElementoCatalogo> CrearAsync(string tipo, DatosCatalogo datos, CancellationToken cancellationToken);

    /// <summary>PUT /catalogos/{tipo}/{id}: reemplaza los datos; <see cref="DatosCatalogo.Activo"/> nulo conserva el estado.</summary>
    Task<ElementoCatalogo> ActualizarAsync(string tipo, Guid id, DatosCatalogo datos, CancellationToken cancellationToken);
}

/// <summary>Valores de <c>{tipo}</c> en /catalogos/{tipo}.</summary>
public static class TiposCatalogo
{
    public const string TiposEquipo = "tipos-equipo";
    public const string Ubicaciones = "ubicaciones";
    public const string Proveedores = "proveedores";
    public const string TiposFalla = "tipos-falla";
}

/// <summary>Elemento de un catálogo; los tipos de equipo traen sus valores regulatorios por defecto (RF-030).</summary>
public sealed record ElementoCatalogo(
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
    // La caja de la lista desplegable y el lector de pantalla muestran el elemento con ToString().
    public override string ToString() => Nombre;

    /// <summary>Los mismos datos para un PUT, con el estado indicado (null lo conserva).</summary>
    public DatosCatalogo ComoDatos(bool? activo = null) => new(
        Nombre, Descripcion, Nit, TipoProveedor, NombreContacto, Correo, Telefono,
        ClaseRiesgo, RequiereCalibracion, PeriodicidadCalibracionMeses, activo);
}

/// <summary>Cuerpo de POST y PUT de /catalogos/{tipo} (cada catálogo usa sus campos).</summary>
public sealed record DatosCatalogo(
    string Nombre,
    string? Descripcion = null,
    string? Nit = null,
    string? TipoProveedor = null,
    string? NombreContacto = null,
    string? Correo = null,
    string? Telefono = null,
    string? ClaseRiesgo = null,
    bool? RequiereCalibracion = null,
    int? PeriodicidadCalibracionMeses = null,
    bool? Activo = null);

public sealed class ServicioCatalogosHttp(ClienteApi cliente) : IServicioCatalogos
{
    public async Task<IReadOnlyList<ElementoCatalogo>> ListarAsync(string tipo, CancellationToken cancellationToken) =>
        await PeticionApi.EnviarAsync<List<ElementoCatalogo>>(
            cliente, HttpMethod.Get, Ruta(tipo), null,
            estado => estado == 403 ? "Tu rol no permite consultar los catálogos." : null,
            cancellationToken);

    public Task<ElementoCatalogo> ObtenerAsync(string tipo, Guid id, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ElementoCatalogo>(cliente, HttpMethod.Get, $"{Ruta(tipo)}/{id}", null, MensajeGestion, cancellationToken);

    public Task<ElementoCatalogo> CrearAsync(string tipo, DatosCatalogo datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ElementoCatalogo>(cliente, HttpMethod.Post, Ruta(tipo), datos, MensajeGestion, cancellationToken);

    public Task<ElementoCatalogo> ActualizarAsync(string tipo, Guid id, DatosCatalogo datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ElementoCatalogo>(cliente, HttpMethod.Put, $"{Ruta(tipo)}/{id}", datos, MensajeGestion, cancellationToken);

    private static string Ruta(string tipo) => $"catalogos/{Uri.EscapeDataString(tipo)}";

    // 403 y 409 traen su detalle desde la API («Solo el Ingeniero Biomédico gestiona…», CAT-001).
    private static string? MensajeGestion(int estado) => estado switch
    {
        404 => "Ese valor ya no existe en el catálogo. Actualiza la lista.",
        _ => null,
    };
}
