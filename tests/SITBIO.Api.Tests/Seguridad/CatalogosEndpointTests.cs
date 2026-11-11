using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de GET /api/v1/catalogos/{tipo} (API-CAT, consulta): AC-01 y AC-02 consultan; los
/// demás roles reciben 403 (RNF-003). Sin base de datos.
/// </summary>
public class CatalogosEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/catalogos";
    private readonly IRepositorioCatalogos _repositorio = Substitute.For<IRepositorioCatalogos>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();

    private HttpClient Cliente(string? codigoRol)
    {
        _repositorio.TiposEquipoAsync(Arg.Any<CancellationToken>())
            .Returns([new CatalogoDto(Guid.NewGuid(), "Tensiómetro", true, "I", true, 12)]);
        _repositorio.UbicacionesAsync(Arg.Any<CancellationToken>()).Returns([new CatalogoDto(Guid.NewGuid(), "Urgencias", true)]);
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s.AddScoped(_ => _repositorio).AddScoped(_ => _sesiones).AddScoped(_ => _bitacora)))
            .CreateClient();

        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var usuario = new Usuario(Guid.NewGuid(), 1, "Sesión", "Prueba", "sesion@demo.co", "hash");
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    [Fact]
    public async Task SinToken_Responde401()
    {
        var respuesta = await Cliente(null).GetAsync($"{Ruta}/tipos-equipo");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-02")]
    public async Task AdministradorEIngeniero_ConsultanLosTiposConSusValoresPorDefecto(string rol)
    {
        var tipos = await Cliente(rol).GetFromJsonAsync<CatalogoDto[]>($"{Ruta}/tipos-equipo");

        var tipo = Assert.Single(tipos!);
        Assert.Equal("Tensiómetro", tipo.Nombre);
        Assert.Equal("I", tipo.ClaseRiesgo);
        Assert.Equal(12, tipo.PeriodicidadCalibracionMeses);
    }

    [Theory]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData("AC-07")]
    public async Task OtrosRoles_NoConsultanCatalogos(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}/ubicaciones");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _repositorio.DidNotReceive().UbicacionesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CatalogoDesconocido_Responde404()
    {
        var respuesta = await Cliente("AC-02").GetAsync($"{Ruta}/marcas");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
