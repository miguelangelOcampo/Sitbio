using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Analitica;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Indicadores;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Reportes;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de J-03: POST /reportes/indicadores.pdf (roles de Reportes e indicadores: AC-01, AC-02, AC-05 a
/// AC-07) y POST /reportes/analitica.pdf (roles de Analítica: AC-02, AC-05 a AC-07) con los gráficos en PNG. Sin
/// token, 401; otro rol, 403; imágenes que no son PNG, muy pesadas o de más, 400. Los datos salen de los
/// repositorios del tenant del token (sustitutos aquí, sin base de datos).
/// </summary>
public class ExportacionGraficosEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    private static readonly string RutaIndicadores = $"{RutasApi.V1}/reportes/indicadores.pdf";
    private static readonly string RutaAnalitica = $"{RutasApi.V1}/reportes/analitica.pdf";

    private readonly IRepositorioIndicadores _indicadores = Substitute.For<IRepositorioIndicadores>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioAnalitica _analitica = Substitute.For<IRepositorioAnalitica>();
    private readonly IPlacasEquipos _placas = Substitute.For<IPlacasEquipos>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly WebApplicationFactory<Program> _fabrica;

    public ExportacionGraficosEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        _indicadores.DatosAsync(default, default, default).ReturnsForAnyArgs(new DatosIndicadores(4, 3, [TimeSpan.FromHours(5)], 2, 10, 1));
        _indicadores.ParqueAsync(default).ReturnsForAnyArgs(new ParqueDto(8, 1, 1, []));
        var equipo = Guid.NewGuid();
        var grupos = new List<GrupoDto> { new(0, 1, new CaracteristicasDto(2, 0.5, 1_000_000, 30), "Estables"), new(1, 0, new CaracteristicasDto(0, 0, 0, 0), "Muchas fallas") };
        var equipos = new List<(Guid, int, CaracteristicasDto)> { (equipo, 0, new CaracteristicasDto(2, 0.5, 1_000_000, 30)) };
        _analitica.UltimoAsync(default).ReturnsForAnyArgs(new ResultadoAnalitica(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 2, grupos, equipos, [new AgrupacionGuardada(2, 1, grupos, equipos)]));
        _placas.PlacasAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, DatosEquipo> { [equipo] = new("PL-1", "Monitor") });
    }

    private HttpClient Cliente(string? codigoRol)
    {
        var cliente = _fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _indicadores)
                    .AddScoped(_ => _intervenciones)
                    .AddScoped(_ => _analitica)
                    .AddScoped(_ => _placas)
                    .AddScoped(_ => _instituciones)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)
                    .AddScoped(_ => _auditoria)))
            .CreateClient();

        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    private static SolicitudPdfIndicadores Indicadores(params GraficoAdjunto[] graficos) =>
        new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), 6, graficos);

    private static SolicitudPdfAnalitica Analitica(params GraficoAdjunto[] graficos) => new(2, "fallas", "costo", graficos);

    private static async Task AssertPdfAsync(HttpResponseMessage respuesta, string nombre)
    {
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal(nombre, respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var pdf = await respuesta.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task Indicadores_LoExportanLosRolesDeLaPantalla(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync(RutaIndicadores, Indicadores(new GraficoAdjunto("Mantenimientos por mes", Png1x1)));

        await AssertPdfAsync(respuesta, "indicadores-20261101-20261120.pdf");
        await _auditoria.Received(1).RegistrarAsync(
            _usuario.Id, AccionesAuditoria.Exportar, ExportacionGraficos.EntidadIndicadores, Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task Analitica_LaExportanLosRolesDeLaPantalla(string rol) =>
        await AssertPdfAsync(await Cliente(rol).PostAsJsonAsync(RutaAnalitica, Analitica(new GraficoAdjunto("Dispersión", Png1x1))), "analitica-2-grupos.pdf");

    [Theory]
    [InlineData(CodigosRol.TecnicoMantenimiento, "indicadores")]
    [InlineData(CodigosRol.PersonalClinico, "indicadores")]
    [InlineData(CodigosRol.TecnicoMantenimiento, "analitica")]
    [InlineData(CodigosRol.PersonalClinico, "analitica")]
    [InlineData(CodigosRol.AdministradorSistema, "analitica")]
    public async Task OtrosRoles_Reciben403_YNoSeConsultaNada(string rol, string reporte)
    {
        var cliente = Cliente(rol);
        var respuesta = reporte == "indicadores"
            ? await cliente.PostAsJsonAsync(RutaIndicadores, Indicadores())
            : await cliente.PostAsJsonAsync(RutaAnalitica, Analitica());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _indicadores.DidNotReceiveWithAnyArgs().DatosAsync(default, default, default);
        await _analitica.DidNotReceiveWithAnyArgs().UltimoAsync(default);
    }

    [Fact]
    public async Task SinToken_Responde401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null).PostAsJsonAsync(RutaIndicadores, Indicadores())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null).PostAsJsonAsync(RutaAnalitica, Analitica())).StatusCode);
    }

    [Fact]
    public async Task ImagenesQueNoSonPng_MuyPesadas_ODeMas_Responden400()
    {
        var cliente = Cliente(CodigosRol.IngenieroBiomedico);
        var jpeg = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46]);
        var pesada = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[ExportacionGraficos.MaximoBytesImagen]]);
        var siete = Enumerable.Repeat(new GraficoAdjunto("x", Png1x1), ExportacionGraficos.MaximoGraficos + 1).ToArray();

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync(RutaIndicadores, Indicadores(new GraficoAdjunto("Jpeg", jpeg)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync(RutaAnalitica, Analitica(new GraficoAdjunto("Pesada", pesada)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync(RutaAnalitica, Analitica(siete))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync(RutaAnalitica, new SolicitudPdfAnalitica(2, "edad", null, null))).StatusCode);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }
}
