using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de GET y PUT /api/v1/tenant/actual (API-TEN, HU-024): solo el Administrador del
/// Sistema (AC-01); los demás roles reciben 403 (RNF-003). Sin base de datos.
/// </summary>
public class InstitucionEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/tenant/actual";
    private readonly IRepositorioInstitucion _repositorio = Substitute.For<IRepositorioInstitucion>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();

    private HttpClient Cliente(string? codigoRol)
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        void Fijar(string propiedad, object? valor) =>
            typeof(Tenant).GetProperty(propiedad)!.SetValue(tenant, valor, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
        Fijar(nameof(Tenant.Plan), "Clínica");
        Fijar(nameof(Tenant.MaxUsuariosGestion), 7);
        Fijar(nameof(Tenant.MaxEquipos), 1000);
        Fijar(nameof(Tenant.MaxAlmacenamientoGb), 25);
        tenant.ActualizarDatos("Clínica San Rafael", "900.123.456-7", 15);

        _repositorio.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(tenant);
        _repositorio.UsoAsync(Arg.Any<CancellationToken>()).Returns(new UsoPlan(6, 420, 0));
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
        var respuesta = await Cliente(null).GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Administrador_VeSuInstitucionConElConsumoDelPlan()
    {
        var respuesta = await Cliente(CodigosRol.AdministradorSistema).GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("900.123.456-7", json.GetProperty("nit").GetString());
        Assert.Equal(15, json.GetProperty("diasAnticipacionAlerta").GetInt32());
        Assert.Equal("Clínica", json.GetProperty("plan").GetString());
        var usuarios = json.GetProperty("consumo").GetProperty("usuariosGestion");
        Assert.Equal((6, 7, 85, true), (usuarios.GetProperty("usados").GetInt32(), usuarios.GetProperty("maximo").GetInt32(),
            usuarios.GetProperty("porcentaje").GetInt32(), usuarios.GetProperty("alerta").GetBoolean()));
        Assert.True(json.GetProperty("consumo").GetProperty("hayAlerta").GetBoolean());
    }

    [Fact]
    public async Task Administrador_ActualizaLosDatos()
    {
        var respuesta = await Cliente(CodigosRol.AdministradorSistema).PutAsJsonAsync(
            Ruta, new { nombre = "Clínica San Rafael IPS", nit = "900.123.456-7", diasAnticipacionAlerta = 30 });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var dto = await respuesta.Content.ReadFromJsonAsync<TenantDto>();
        Assert.Equal(("Clínica San Rafael IPS", 30), (dto!.Nombre, dto.DiasAnticipacionAlerta));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatoInvalido_Responde400ConElCampo()
    {
        var respuesta = await Cliente(CodigosRol.AdministradorSistema).PutAsJsonAsync(
            Ruta, new { nombre = "Clínica San Rafael", nit = "900123", diasAnticipacionAlerta = 15 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("errors").TryGetProperty("nit", out _));
    }

    [Theory]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData("AC-07")]
    public async Task OtrosRoles_NoVenNiEditanLaInstitucion(string rol)
    {
        var cliente = Cliente(rol);

        var consulta = await cliente.GetAsync(Ruta);
        var edicion = await cliente.PutAsJsonAsync(Ruta, new { nombre = "Otra IPS", nit = "900.123.456-7", diasAnticipacionAlerta = 30 });

        Assert.Equal(HttpStatusCode.Forbidden, consulta.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edicion.StatusCode);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }
}
