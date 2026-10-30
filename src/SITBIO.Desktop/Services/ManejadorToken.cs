using System.Net.Http;
using System.Net.Http.Headers;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Adjunta el JWT de la sesión a cada solicitud hacia la API (ADR-003). El tenant y el
/// rol viajan dentro del token; el cliente nunca los envía en el cuerpo ni en la URL.
/// </summary>
public sealed class ManejadorToken(SesionActual sesion) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (sesion.Sesion is { } actual)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actual.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
