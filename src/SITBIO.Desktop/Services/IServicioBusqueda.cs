using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Búsqueda global de la barra superior (D-04): equipos, tiquetes y órdenes de la institución.</summary>
public interface IServicioBusqueda
{
    /// <summary>GET /busqueda?q=: hasta cinco resultados por grupo; la API decide qué ve cada rol.</summary>
    Task<ResultadoBusqueda> BuscarAsync(string texto, CancellationToken cancellationToken);
}

public sealed record ResultadoBusqueda(
    IReadOnlyList<EquipoEncontrado> Equipos,
    IReadOnlyList<TiqueteEncontrado> Tiquetes,
    IReadOnlyList<OrdenEncontrada> Ordenes);

public sealed record EquipoEncontrado(Guid Id, string PlacaInventario, string NumeroSerie, string TipoEquipo, string Ubicacion);

public sealed record TiqueteEncontrado(string Codigo, string TipoFalla, string Estado, string PlacaInventario);

/// <summary>Referencia = código del tiquete de origen o, en una preventiva, el código de activo del equipo.</summary>
public sealed record OrdenEncontrada(Guid Id, string Referencia, string TipoMantenimiento, string Estado, string PlacaInventario);

public sealed class ServicioBusquedaHttp(ClienteApi cliente) : IServicioBusqueda
{
    public Task<ResultadoBusqueda> BuscarAsync(string texto, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<ResultadoBusqueda>(
            cliente, HttpMethod.Get, $"busqueda?q={Uri.EscapeDataString(texto)}", null, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Tu rol no permite buscar en el sistema.",
        >= 500 => "El servidor no pudo completar la búsqueda. Intenta de nuevo en unos minutos.",
        _ => null,
    };
}
