using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Api.Modules.Instituciones;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Api.Tests.Instituciones;

/// <summary>
/// J-06: aviso por correo de cada solicitud del servicio (J-05, RF-029) con un emisor sustituto. El correo va al
/// destino configurado con los datos de la solicitud; un fallo se reintenta una vez, no pierde la solicitud y el
/// registro no lleva datos personales; sin configuración no se intenta enviar.
/// </summary>
public class AvisoSolicitudesTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly OpcionesCorreo Configurado = new() { Servidor = "localhost", Remitente = "avisos@sitbio.co", Destino = "proveedor@sitbio.co" };

    private readonly IEnviadorCorreo _enviador = Substitute.For<IEnviadorCorreo>();
    private readonly RegistroEnMemoria _registro = new();

    private static AvisoSolicitud Aviso() => new(
        "SOL-000031", "demostracion", "Clínica", "Clínica San Rafael", "900.123.456-7", "Ana Gómez", "ana@sanrafael.co", null, "Hola",
        DateTimeOffset.UtcNow);

    private ServicioAvisosSolicitudes Servicio(OpcionesCorreo opciones) =>
        new(new ColaAvisosSolicitudes(), _enviador, opciones, _registro) { EsperaReintento = TimeSpan.Zero };

    [Fact]
    public async Task Envia_AlDestinoConfiguradoConLosDatosDeLaSolicitud()
    {
        Assert.True(await Servicio(Configurado).EnviarAsync(Aviso(), CancellationToken.None));

        await _enviador.Received(1).EnviarAsync(
            Arg.Is<MensajeCorreo>(m => m.Para == "proveedor@sitbio.co" && m.ResponderA == "ana@sanrafael.co" && m.Asunto.Contains("SOL-000031") && m.Texto.Contains("Clínica San Rafael")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalloPasajero_SeReintentaUnaVez()
    {
        _enviador.EnviarAsync(default!, default).ReturnsForAnyArgs(_ => throw new IOException("conexión cerrada"), _ => Task.CompletedTask);

        Assert.True(await Servicio(Configurado).EnviarAsync(Aviso(), CancellationToken.None));

        await _enviador.ReceivedWithAnyArgs(2).EnviarAsync(default!, default);
    }

    [Fact]
    public async Task FalloPersistente_NoSeEnvia_YElRegistroNoLlevaDatosPersonales()
    {
        _enviador.EnviarAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("550 ana@sanrafael.co rechazado"));

        Assert.False(await Servicio(Configurado).EnviarAsync(Aviso(), CancellationToken.None));

        await _enviador.ReceivedWithAnyArgs(ServicioAvisosSolicitudes.Intentos).EnviarAsync(default!, default);
        var errores = _registro.Entradas.Where(e => e.Nivel == LogLevel.Error).ToList();
        Assert.Equal(2, errores.Count);
        Assert.All(errores, e =>
        {
            Assert.Contains("SOL-000031", e.Texto, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), e.Texto, StringComparison.Ordinal);
            Assert.Null(e.Error);
            foreach (var personal in new[] { "ana@sanrafael.co", "Ana Gómez", "Clínica San Rafael", "900.123.456-7" })
            {
                Assert.DoesNotContain(personal, e.Texto, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public async Task SinConfiguracion_NoIntentaEnviar()
    {
        Assert.False(await Servicio(new OpcionesCorreo { Servidor = "smtp.gmail.com", Destino = "proveedor@sitbio.co" }).EnviarAsync(Aviso(), CancellationToken.None));

        await _enviador.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning && e.Texto.Contains("SOL-000031", StringComparison.Ordinal));
    }

    private HttpClient Cliente(IRepositorioSolicitudesServicio repositorio) => fabrica.WithWebHostBuilder(b => b
            .UseSetting(SolicitudesServicioEndpoints.SeccionLimite, "100")
            .ConfigureTestServices(s =>
            {
                s.AddScoped(_ => repositorio);
                s.AddScoped(_ => Substitute.For<IRepositorioInstitucion>());
                s.AddScoped(_ => Substitute.For<IBitacoraAuditoria>());
                s.RemoveAll<OpcionesCorreo>();
                s.AddSingleton(Configurado);
                s.RemoveAll<IEnviadorCorreo>();
                s.AddSingleton(_enviador);
            }))
        .CreateClient();

    private static object Solicitud() => new
    {
        tipo = "cotizacion",
        institucion = "Clínica San Rafael",
        nit = "900.123.456-7",
        contacto = "Ana Gómez",
        correo = "ana@sanrafael.co",
        planSolicitado = "Hospital",
    };

    private static IRepositorioSolicitudesServicio Repositorio()
    {
        var repositorio = Substitute.For<IRepositorioSolicitudesServicio>();
        repositorio.When(r => r.RegistrarAsync(Arg.Any<SolicitudServicio>(), Arg.Any<CancellationToken>()))
            .Do(llamada => llamada.Arg<SolicitudServicio>().Numerar(44));
        return repositorio;
    }

    private async Task EsperarEnviosAsync(int cantidad)
    {
        for (var i = 0; i < 100 && _enviador.ReceivedCalls().Count() < cantidad; i++)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Post_GuardaLaSolicitud_YElCorreoSaleEnSegundoPlano()
    {
        var repositorio = Repositorio();
        var respuesta = await Cliente(repositorio).PostAsJsonAsync($"{RutasApi.V1}/solicitudes-servicio", Solicitud());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        await EsperarEnviosAsync(1);
        await _enviador.Received(1).EnviarAsync(
            Arg.Is<MensajeCorreo>(m => m.Para == "proveedor@sitbio.co" && m.Asunto == "[SITBIO] SOL-000044 · Cotización · Hospital · Clínica San Rafael"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_ConElCorreoCaido_RespondeCreadaYLaSolicitudQuedaGuardada()
    {
        var repositorio = Repositorio();
        _enviador.EnviarAsync(default!, default).ThrowsAsyncForAnyArgs(new IOException("sin red"));

        var respuesta = await Cliente(repositorio).PostAsJsonAsync($"{RutasApi.V1}/solicitudes-servicio", Solicitud());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        await repositorio.Received(1).RegistrarAsync(Arg.Any<SolicitudServicio>(), Arg.Any<CancellationToken>());
        await EsperarEnviosAsync(1);
        await _enviador.ReceivedWithAnyArgs().EnviarAsync(default!, default);
    }

    /// <summary>Registro que guarda el texto ya formateado de cada entrada.</summary>
    private sealed class RegistroEnMemoria : ILogger<ServicioAvisosSolicitudes>
    {
        public List<(LogLevel Nivel, string Texto, Exception? Error)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entradas)
            {
                Entradas.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
