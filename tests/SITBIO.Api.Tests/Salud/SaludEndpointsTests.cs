using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sitbio.Api.Modules.Salud;

namespace Sitbio.Api.Tests.Salud;

public class SaludEndpointsTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetSalud_RespondeOk()
    {
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/api/v1/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var estado = await respuesta.Content.ReadFromJsonAsync<EstadoServicio>();
        Assert.Equal("ok", estado?.Estado);
    }
}
