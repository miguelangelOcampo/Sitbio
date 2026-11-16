using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>GET y PUT /tenant/actual sobre <see cref="ClienteApi"/> (lleva el token).</summary>
public sealed class ServicioInstitucionHttp(ClienteApi cliente) : IServicioInstitucion
{
    private const string Ruta = "tenant/actual";

    public Task<Institucion> ObtenerAsync(CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Institucion>(cliente, HttpMethod.Get, Ruta, null, Mensaje, cancellationToken);

    /// <summary>Los 400 por campo y el 409 TEN-001 (NIT de otra institución) llegan con el texto de la API.</summary>
    public Task<Institucion> ActualizarAsync(DatosInstitucion datos, CancellationToken cancellationToken) =>
        PeticionApi.EnviarAsync<Institucion>(cliente, HttpMethod.Put, Ruta, datos, Mensaje, cancellationToken);

    private static string? Mensaje(int estado) => estado switch
    {
        403 => "Solo el Administrador del Sistema configura la institución.",
        404 => "No encontramos los datos de tu institución. Comunícate con el proveedor del servicio.",
        _ => null,
    };
}
