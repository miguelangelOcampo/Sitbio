using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Motor de campos dinámicos (API-CFG, HU-006): consulta de la versión activa y publicación de una nueva.</summary>
public interface IServicioConfiguracionCampos
{
    /// <summary>GET /configuracion-campos/{tipoEquipoId}: versión activa (o ninguna) e historial.</summary>
    Task<ConfiguracionCampos> ObtenerAsync(Guid tipoEquipoId, CancellationToken cancellationToken);

    /// <summary>POST /configuracion-campos/{tipoEquipoId}: publica la lista completa como versión nueva (solo AC-02).</summary>
    Task<ConfiguracionCampos> PublicarAsync(Guid tipoEquipoId, IReadOnlyList<CampoDinamico> campos, CancellationToken cancellationToken);
}

/// <summary>Valores de <c>tipoDato</c> (script 02).</summary>
public static class TiposDatoCampo
{
    public const string Numero = "numero";
    public const string NumeroDecimal = "numero_decimal";
    public const string Texto = "texto";
    public const string Booleano = "booleano";
    public const string Fecha = "fecha";
    public const string Seleccion = "seleccion";

    public static bool EsNumerico(string? tipo) => tipo is Numero or NumeroDecimal;
}

/// <summary>Campo del formulario de intervención; <see cref="Nombre"/> es la clave dentro de <c>camposDinamicos</c>.</summary>
public sealed record CampoDinamico(
    string Nombre,
    string Etiqueta,
    string TipoDato,
    bool Obligatorio,
    IReadOnlyList<string>? Opciones = null,
    string? Unidad = null);

public sealed record ResumenVersion(int Version, string Estado, DateTimeOffset FechaCreacion, int Campos);

/// <summary>Respuesta de API-CFG. <see cref="Version"/> nula = el tipo aún no tiene campos publicados.</summary>
public sealed record ConfiguracionCampos(
    Guid TipoEquipoId,
    string TipoEquipo,
    int? Version,
    DateTimeOffset? FechaPublicacion,
    IReadOnlyList<CampoDinamico> Campos,
    IReadOnlyList<ResumenVersion> Versiones);

public sealed class ServicioConfiguracionCamposHttp(ClienteApi cliente) : IServicioConfiguracionCampos
{
    public Task<ConfiguracionCampos> ObtenerAsync(Guid tipoEquipoId, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ConfiguracionCampos>(cliente, HttpMethod.Get, Ruta(tipoEquipoId), null, Mensaje, cancellationToken);

    public Task<ConfiguracionCampos> PublicarAsync(Guid tipoEquipoId, IReadOnlyList<CampoDinamico> campos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ConfiguracionCampos>(cliente, HttpMethod.Post, Ruta(tipoEquipoId), new { campos }, Mensaje, cancellationToken);

    private static string Ruta(Guid tipoEquipoId) => $"configuracion-campos/{tipoEquipoId}";

    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Solo el Ingeniero Biomédico define y publica los campos dinámicos; puedes consultarlos.",
        404 => "Ese tipo de equipo ya no existe en tu institución. Actualiza la lista.",
        _ => null,
    };
}
