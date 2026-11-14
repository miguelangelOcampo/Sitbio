using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>/equipos sobre <see cref="ClienteApi"/> (lleva el token): consulta (API-EQ-LIST, API-EQ), registro y edición.</summary>
public sealed class ServicioEquiposHttp(ClienteApi cliente) : IServicioEquipos
{
    public Task<PaginaEquipos> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<PaginaEquipos>(cliente, HttpMethod.Get, Ruta(filtro), null, MensajeConsulta, cancellationToken);

    public Task<Equipo> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Equipo>(cliente, HttpMethod.Get, $"equipos/{id}", null, MensajeConsulta, cancellationToken);

    public Task<Equipo> CrearAsync(DatosEquipo datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Equipo>(cliente, HttpMethod.Post, "equipos", datos, MensajeGestion, cancellationToken);

    public Task<Equipo> ActualizarAsync(Guid id, DatosEquipo datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Equipo>(cliente, HttpMethod.Put, $"equipos/{id}", datos, MensajeGestion, cancellationToken);

    public Task<LineaTiempo> LineaTiempoAsync(Guid id, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<LineaTiempo>(cliente, HttpMethod.Get, $"equipos/{id}/linea-tiempo", null, MensajeConsulta, cancellationToken);

    /// <summary>Ruta con los filtros presentes; los textos van escapados.</summary>
    public static string Ruta(FiltroEquipos filtro)
    {
        var partes = new List<string>();
        void Agregar(string nombre, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                partes.Add($"{nombre}={Uri.EscapeDataString(valor.Trim())}");
            }
        }

        Agregar("placa", filtro.Placa);
        Agregar("serie", filtro.Serie);
        Agregar("estado", filtro.Estado);
        Agregar("clase", filtro.Clase);
        if (filtro.Calibracion is { } calibracion)
        {
            partes.Add($"calibracion={(calibracion ? "true" : "false")}");
        }

        partes.Add($"page={filtro.Pagina}");
        partes.Add($"pageSize={filtro.Tamano}");
        return "equipos?" + string.Join("&", partes);
    }

    private static string? MensajeConsulta(int estado) => estado switch
    {
        403 => "Tu rol no permite consultar el inventario de equipos.",
        404 => NoEncontrado,
        >= 500 => "El servidor no pudo completar la consulta. Intenta de nuevo en unos minutos.",
        _ => null,
    };

    private static string? MensajeGestion(int estado) => estado switch
    {
        403 => "Solo el Ingeniero Biomédico registra y edita equipos.",
        404 => NoEncontrado,
        _ => null,
    };

    private const string NoEncontrado = "El equipo no existe o no pertenece a tu institución.";
}
