using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Planes del servicio y solicitud de demostración o cotización (API-SERV-PLANES, API-SERV-SOL;
/// HU-028). Son públicos: se usan desde el inicio de sesión, sin token.
/// </summary>
public interface IServicioSolicitudesServicio
{
    Task<IReadOnlyList<PlanOfrecido>> PlanesAsync(CancellationToken cancellationToken);

    Task<SolicitudEnviada> EnviarAsync(DatosSolicitud datos, CancellationToken cancellationToken);
}

/// <summary>PlanServicioDto de la API; los límites nulos son «a la medida» y el valor nulo, «Cotización».</summary>
public sealed record PlanOfrecido(string Nombre, int? MaxUsuariosGestion, int? MaxEquipos, int? MaxAlmacenamientoGb, decimal? ValorMensualCop);

/// <summary>Cuerpo de POST /solicitudes-servicio.</summary>
public sealed record DatosSolicitud(
    string Tipo,
    string Institucion,
    string Nit,
    string Contacto,
    string Correo,
    string? Telefono,
    string? PlanSolicitado,
    string? Mensaje);

/// <summary>Código con el que el proveedor atiende la solicitud (MSG-021).</summary>
public sealed record SolicitudEnviada(string Codigo, string Tipo, string Estado, string? PlanSolicitado, DateTimeOffset FechaCreacion);

/// <summary>GET /planes y POST /solicitudes-servicio sobre <see cref="ClienteApi"/> (sin sesión no lleva token).</summary>
public sealed class ServicioSolicitudesServicioHttp(ClienteApi cliente) : IServicioSolicitudesServicio
{
    public async Task<IReadOnlyList<PlanOfrecido>> PlanesAsync(CancellationToken cancellationToken) =>
        await PeticionApi.EnviarAsync<List<PlanOfrecido>>(cliente, HttpMethod.Get, "planes", null, Mensaje, cancellationToken);

    /// <summary>Los 400 por campo llegan con el texto de la API; el 429, con el aviso de esperar.</summary>
    public Task<SolicitudEnviada> EnviarAsync(DatosSolicitud datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<SolicitudEnviada>(cliente, HttpMethod.Post, "solicitudes-servicio", datos, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado switch
    {
        429 => "Enviaste varias solicitudes seguidas. Espera un minuto e intenta de nuevo.",
        _ => null,
    };
}
