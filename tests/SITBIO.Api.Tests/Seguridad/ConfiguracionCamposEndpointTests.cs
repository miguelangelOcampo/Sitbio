using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Catalogos;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de /api/v1/configuracion-campos (API-CFG, HU-006): AC-01, AC-02 y AC-03 consultan;
/// solo AC-02 publica (CA-HU-006-03); los demás roles reciben 403. Sin base de datos.
/// </summary>
public class ConfiguracionCamposEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/configuracion-campos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IRepositorioConfiguracionCampos _repositorio = Substitute.For<IRepositorioConfiguracionCampos>();
    private readonly IRepositorioCatalogos _catalogos = Substitute.For<IRepositorioCatalogos>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly TipoEquipo _bomba = new(Guid.NewGuid(), "Bomba de infusión");

    private static readonly object Cuerpo = new
    {
        campos = new object[]
        {
            new { etiqueta = "Tasa de goteo", tipoDato = "numero_decimal", obligatorio = true, unidad = "ml/h" },
            new { etiqueta = "Resultado", tipoDato = "seleccion", opciones = new[] { "Pasa", "No pasa" } },
        },
    };

    private HttpClient Cliente(string? codigoRol)
    {
        _catalogos.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IElementoCatalogo?)null);
        _catalogos.ObtenerAsync(TiposCatalogo.TiposEquipo, _bomba.Id, Arg.Any<CancellationToken>()).Returns(_bomba);
        _repositorio.VersionesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _repositorio)
                    .AddScoped(_ => _catalogos)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)))
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
        var respuesta = await Cliente(null).GetAsync($"{Ruta}/{_bomba.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    public async Task AdministradorIngenieroYTecnico_ConsultanLaVersionActiva(string rol)
    {
        var cliente = Cliente(rol);
        _repositorio.VersionesAsync(_bomba.Id, Arg.Any<CancellationToken>()).Returns([
            new VersionConfiguracion(_bomba.Id, 1, EstadosConfiguracion.Activa, Guid.NewGuid(), DateTimeOffset.UtcNow,
                [new CampoDinamico("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h")]),
        ]);

        var configuracion = await cliente.GetFromJsonAsync<ConfiguracionCamposDto>($"{Ruta}/{_bomba.Id}", Json);

        Assert.Equal(("Bomba de infusión", (int?)1), (configuracion!.TipoEquipo, configuracion.Version));
        Assert.Equal("tasa_goteo", Assert.Single(configuracion.Campos).Nombre);
    }

    [Theory]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData("AC-07")]
    public async Task OtrosRoles_NoConsultan(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}/{_bomba.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Ingeniero_PublicaUnaVersion_Responde201()
    {
        var respuesta = await Cliente("AC-02").PostAsJsonAsync($"{Ruta}/{_bomba.Id}", Cuerpo);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal($"{Ruta}/{_bomba.Id}", respuesta.Headers.Location!.OriginalString);
        var publicada = await respuesta.Content.ReadFromJsonAsync<ConfiguracionCamposDto>(Json);
        Assert.Equal(1, publicada!.Version);
        Assert.Equal(["tasa_de_goteo", "resultado"], publicada.Campos.Select(c => c.Nombre));
        await _repositorio.Received(1).PublicarAsync(Arg.Is<VersionConfiguracion>(v => v.Version == 1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-03")]
    [InlineData("AC-05")]
    public async Task CA_HU_006_03_OtrosRoles_NoPublican(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync($"{Ruta}/{_bomba.Id}", Cuerpo);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _repositorio.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
    }

    [Fact]
    public async Task Publicar_CamposInvalidos_Responde400ConElCampo()
    {
        var respuesta = await Cliente("AC-02").PostAsJsonAsync($"{Ruta}/{_bomba.Id}", new { campos = new[] { new { etiqueta = "PA", tipoDato = "numero" } } });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("campos[0].etiqueta", out _));
    }

    [Fact]
    public async Task TipoDeOtraInstitucion_Responde404()
    {
        var respuesta = await Cliente("AC-02").GetAsync($"{Ruta}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
