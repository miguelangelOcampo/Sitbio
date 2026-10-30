using System.Net.Http;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Cliente de la API para los módulos que requieren sesión: lleva el token por
/// <see cref="ManejadorToken"/>. Los servicios de cada historia lo reciben por inyección.
/// </summary>
public sealed class ClienteApi(HttpClient http)
{
    public HttpClient Http { get; } = http;
}
