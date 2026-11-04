using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>Contrato HTTP de GET /api/v1/bitacora (CA-HU-010-03) y resistencia del registro a fallos, sin bases de datos.</summary>
public class AuditoriaEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/bitacora";

    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IDirectorioUsuarios _directorio = Substitute.For<IDirectorioUsuarios>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Sofía", "Herrera", "sofia@demo.co", "hash");

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.EstaActivaAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s.AddScoped(_ => _bitacora).AddScoped(_ => _directorio).AddScoped(_ => _sesiones)))
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
    [InlineData("AC-01")]
    [InlineData("AC-07")]
    public async Task CA_HU_010_03_AdministradorYAuditor_ConsultanConNombreDelUsuario(string rol)
    {
        var evento = EventoAuditoria.Nuevo(_usuario.Id, AccionesAuditoria.AccesoDenegado, "usuarios", DateTimeOffset.UtcNow);
        _bitacora.ConsultarAsync(Arg.Any<FiltroBitacora>(), Arg.Any<CancellationToken>()).Returns(([evento], 1L));
        _directorio.NombresAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [_usuario.Id] = "Sofía Herrera" });

        var respuesta = await Cliente(rol).GetAsync($"{Ruta}?usuarioId={_usuario.Id}&accion=ACCESO_DENEGADO&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var item = cuerpo.RootElement.GetProperty("items")[0];
        Assert.Equal("Sofía Herrera", item.GetProperty("usuario").GetString());
        Assert.Equal("ACCESO_DENEGADO", item.GetProperty("accion").GetString());
        Assert.Equal(1, cuerpo.RootElement.GetProperty("total").GetInt32());
        await _bitacora.Received(1).ConsultarAsync(
            Arg.Is<FiltroBitacora>(f => f.UsuarioId == _usuario.Id && f.Accion == "ACCESO_DENEGADO" && f.Tamano == 20),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    public async Task OtroRol_Responde403YQuedaRegistrado(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _bitacora.DidNotReceiveWithAnyArgs().ConsultarAsync(default!, default);
        await _bitacora.Received(1).RegistrarAsync(
            Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoDenegado && e.Entidad == "bitacora" && e.UsuarioId == _usuario.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinToken_Responde401SinRegistrar()
    {
        var respuesta = await Cliente(null).GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        await _bitacora.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Theory]
    [InlineData("?accion=INVENTADA")]
    [InlineData("?desde=2026-11-03T10:00:00Z&hasta=2026-11-03T09:00:00Z")]
    [InlineData("?pageSize=500")]
    public async Task FiltroInvalido_Responde400(string consulta)
    {
        var respuesta = await Cliente("AC-01").GetAsync(Ruta + consulta);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task SiLaBitacoraFalla_El403SeRespondeIgual()
    {
        _bitacora.RegistrarAsync(Arg.Any<EventoAuditoria>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("mongo"));

        var respuesta = await Cliente("AC-02").GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
