using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Adjunta el JWT de la sesión a cada solicitud hacia la API (ADR-003). El tenant y el
/// rol viajan dentro del token; el cliente nunca los envía en el cuerpo ni en la URL.
/// Si la API rechaza el token (401), avisa a la sesión para cerrarla (HU-012).
/// </summary>
public sealed class ManejadorToken(SesionActual sesion) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var actual = sesion.Sesion;
        if (actual is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actual.Token);
        }

        var respuesta = await base.SendAsync(request, cancellationToken);
        if (actual is not null && respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            sesion.NotificarRechazo();
        }

        return respuesta;
    }
}
