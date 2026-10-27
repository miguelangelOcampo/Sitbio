using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sitbio.Api.Errores;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Api.Tests.Errores;

public class ManejadorErroresTests
{
    private static async Task<(int Estado, JsonElement Cuerpo, string? Tipo)> Manejar(Exception excepcion)
    {
        var services = new ServiceCollection().AddLogging().AddManejoErrores().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        var manejador = new ManejadorErrores(
            services.GetRequiredService<IProblemDetailsService>(), NullLogger<ManejadorErrores>.Instance);

        var manejado = await manejador.TryHandleAsync(http, excepcion, CancellationToken.None);

        Assert.True(manejado);
        http.Response.Body.Position = 0;
        var cuerpo = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, cuerpo.RootElement, http.Response.ContentType);
    }

    public static TheoryData<Exception, int> ErroresYCodigos => new()
    {
        { ErrorValidacion.DeCampo("placa", "La placa es obligatoria."), 400 },
        { new ErrorPermiso(), 403 },
        { new ErrorNoEncontrado("El equipo"), 404 },
        { new ErrorConflicto("RN-001", "Ya existe un equipo con esa serie."), 409 },
        { new ErrorServicioNoDisponible(), 503 },
        { new InvalidOperationException("fallo"), 500 },
    };

    [Theory]
    [MemberData(nameof(ErroresYCodigos))]
    public async Task Manejar_TraduceCadaErrorASuCodigo(Exception excepcion, int esperado)
    {
        var (estado, cuerpo, tipo) = await Manejar(excepcion);

        Assert.Equal(esperado, estado);
        Assert.Equal(esperado, cuerpo.GetProperty("status").GetInt32());
        Assert.Equal("application/problem+json", tipo);
        Assert.True(cuerpo.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Conflicto_IncluyeCodigoEstableYMensaje()
    {
        var (_, cuerpo, _) = await Manejar(new ErrorConflicto("RN-009", "Se alcanzó el límite de equipos del plan."));

        Assert.Equal("RN-009", cuerpo.GetProperty("codigo").GetString());
        Assert.Equal("Se alcanzó el límite de equipos del plan.", cuerpo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Validacion_IncluyeErroresPorCampo()
    {
        var (_, cuerpo, _) = await Manejar(ErrorValidacion.DeCampo("placa", "La placa es obligatoria."));

        var mensajes = cuerpo.GetProperty("errors").GetProperty("placa");
        Assert.Equal("La placa es obligatoria.", mensajes[0].GetString());
    }

    [Fact]
    public async Task Inesperado_NoExponeElMensajeInterno()
    {
        var (_, cuerpo, _) = await Manejar(new InvalidOperationException("SELECT * FROM usuario WHERE correo = 'x'"));

        var json = cuerpo.GetRawText();
        Assert.DoesNotContain("SELECT", json);
        Assert.False(cuerpo.TryGetProperty("detail", out _));
        Assert.Equal("Error inesperado", cuerpo.GetProperty("title").GetString());
    }
}
