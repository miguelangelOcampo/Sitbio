using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Equipos;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Reportes;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de API-REP (HU-007, HU-027): el historial de órdenes lo exportan AC-01, AC-02 y AC-05 en PDF o
/// Excel; la hoja de vida en PDF además el Ente Regulador (AC-07). Sin base de datos: todo repositorio que
/// ExportacionReportes resuelve es un sustituto, para que en CI (sin cadenas de conexión) no responda 500.
/// </summary>
public class ReportesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Historial = $"{RutasApi.V1}/reportes/ordenes?desde=2026-11-01&hasta=2026-11-30";
    private readonly IRepositorioReportes _repositorio = Substitute.For<IRepositorioReportes>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IRepositorioLineaTiempo _lineaTiempo = Substitute.For<IRepositorioLineaTiempo>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioEquipos _equipos = Substitute.For<IRepositorioEquipos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly WebApplicationFactory<Program> _fabrica;

    public ReportesEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _repositorio.HistorialOrdenesAsync(default, default, default, default).ReturnsForAnyArgs([]);
    }

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = _fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _repositorio)
                    .AddScoped(_ => _instituciones)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)
                    .AddScoped(_ => _lineaTiempo)
                    .AddScoped(_ => _intervenciones)
                    .AddScoped(_ => _equipos)
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

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema, "pdf", "application/pdf", ".pdf")]
    [InlineData(CodigosRol.IngenieroBiomedico, "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData(CodigosRol.GerenteBiomedico, "pdf", "application/pdf", ".pdf")]
    public async Task Historial_LoExportanAdminIngenieroYGerente(string rol, string formato, string tipo, string extension)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Historial}&formato={formato}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(tipo, respuesta.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(extension, respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.NotEmpty(await respuesta.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task Historial_OtrosRoles_Reciben403(string rol) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(rol).GetAsync($"{Historial}&formato=pdf")).StatusCode);

    [Fact]
    public async Task Historial_SinToken401_YFormatoInvalido400()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null).GetAsync($"{Historial}&formato=pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(CodigosRol.IngenieroBiomedico).GetAsync($"{Historial}&formato=docx")).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    public async Task HojaVida_RolesSinPermiso_Reciben403(string rol) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(rol).GetAsync($"{RutasApi.V1}/equipos/{Guid.NewGuid()}/hoja-vida.pdf")).StatusCode);
}
