using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sitbio.Api.Errores;

namespace Sitbio.Api.Tests.Errores;

public class RespuestasDeErrorTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task RutaInexistente_Responde404ConProblemDetails()
    {
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync($"{RutasApi.V1}/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(404, cuerpo.RootElement.GetProperty("status").GetInt32());
        Assert.True(cuerpo.RootElement.TryGetProperty("traceId", out _));
        Assert.Equal(
            respuesta.Headers.GetValues(CorrelacionMiddleware.Cabecera).Single(),
            cuerpo.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task RutaSinVersion_Responde404()
    {
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/salud");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Correlacion_DevuelveLaRecibida()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(CorrelacionMiddleware.Cabecera, "wpf-1234_abc");

        var respuesta = await cliente.GetAsync($"{RutasApi.V1}/salud");

        Assert.Equal("wpf-1234_abc", respuesta.Headers.GetValues(CorrelacionMiddleware.Cabecera).Single());
    }

    [Fact]
    public async Task Correlacion_NoValida_SeReemplaza()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.TryAddWithoutValidation(CorrelacionMiddleware.Cabecera, "<script>" + new string('x', 80));

        var respuesta = await cliente.GetAsync($"{RutasApi.V1}/salud");

        var id = respuesta.Headers.GetValues(CorrelacionMiddleware.Cabecera).Single();
        Assert.Equal(32, id.Length);
        Assert.True(Guid.TryParseExact(id, "N", out _));
    }
}
