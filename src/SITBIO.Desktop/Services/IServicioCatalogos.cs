using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>Consulta de catálogos maestros (API-CAT, GET /catalogos/{tipo}).</summary>
public interface IServicioCatalogos
{
    Task<IReadOnlyList<ElementoCatalogo>> ListarAsync(string tipo, CancellationToken cancellationToken);
}

/// <summary>Valores de <c>{tipo}</c> en /catalogos/{tipo}.</summary>
public static class TiposCatalogo
{
    public const string TiposEquipo = "tipos-equipo";
    public const string Ubicaciones = "ubicaciones";
    public const string Proveedores = "proveedores";
}

/// <summary>Elemento de un catálogo; los tipos de equipo traen sus valores regulatorios por defecto (RF-030).</summary>
public sealed record ElementoCatalogo(
    Guid Id,
    string Nombre,
    bool Activo,
    string? ClaseRiesgo = null,
    bool? RequiereCalibracion = null,
    int? PeriodicidadCalibracionMeses = null)
{
    // La caja de la lista desplegable y el lector de pantalla muestran el elemento con ToString().
    public override string ToString() => Nombre;
}

public sealed class ServicioCatalogosHttp(ClienteApi cliente) : IServicioCatalogos
{
    public async Task<IReadOnlyList<ElementoCatalogo>> ListarAsync(string tipo, CancellationToken cancellationToken) =>
        await PeticionApi.EnviarAsync<List<ElementoCatalogo>>(
            cliente,
            HttpMethod.Get,
            $"catalogos/{Uri.EscapeDataString(tipo)}",
            null,
            estado => estado == 403 ? "Tu rol no permite consultar los catálogos." : null,
            cancellationToken);
}
