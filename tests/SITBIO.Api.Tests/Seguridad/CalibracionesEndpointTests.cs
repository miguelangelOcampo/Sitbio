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
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de API-CAL (HU-022): el Técnico (AC-03) registra y corrige calibraciones, el Ingeniero
/// (AC-02) las aprueba y las consultan AC-01, AC-02, AC-03, AC-05 y AC-07. Sin base de datos.
/// </summary>
public class CalibracionesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Calibraciones = $"{RutasApi.V1}/calibraciones";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
    private readonly IRepositorioCalibraciones _repositorio = Substitute.For<IRepositorioCalibraciones>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly RegistroCalibracion _calibracion;
    private readonly EquipoDeCalibracion _equipo = new(Guid.NewGuid(), "MON-07", false, true, 12);
    private readonly EquipoDeCalibracion _estetoscopio = new(Guid.NewGuid(), "EST-01", false, false, null);

    public CalibracionesEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _calibracion = new RegistroCalibracion(Guid.NewGuid(), _equipo.Id, Guid.NewGuid(), Hoy.AddDays(-5), "Analizador", RegistroCalibracion.Conforme, Hoy.AddDays(-5).AddMonths(12));
        var leida = new CalibracionLeida(_calibracion, "MON-07", "Monitor", "Urgencias", null, "Ana Mora", null);
        _repositorio.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(leida);
        _repositorio.CalibracionAsync(_calibracion.Id, Arg.Any<CancellationToken>()).Returns(_calibracion);
        _repositorio.EquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(_equipo);
        _repositorio.EquipoAsync(_estetoscopio.Id, Arg.Any<CancellationToken>()).Returns(_estetoscopio);
        _repositorio.ListarAsync(Arg.Any<FiltroCalibraciones>(), Arg.Any<CancellationToken>()).Returns(([leida], 1));
    }

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = _fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _repositorio)
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)))
            .CreateClient();

        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    [Fact]
    public async Task Tecnico_RegistraYCorrige_PeroNoAprueba()
    {
        var cliente = Cliente(CodigosRol.TecnicoMantenimiento);

        var creada = await cliente.PostAsJsonAsync(Calibraciones, new { equipoId = _equipo.Id, fechaCalibracion = Hoy.AddDays(-1), patronReferencia = "Analizador Fluke" });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        Assert.StartsWith($"{RutasApi.V1}/calibraciones/", creada.Headers.Location!.OriginalString);
        _repositorio.Received(1).Agregar(Arg.Is<RegistroCalibracion>(c => c.EquipoId == _equipo.Id && c.FechaVencimiento == Hoy.AddDays(-1).AddMonths(12)));

        var corregida = await cliente.PatchAsJsonAsync($"{Calibraciones}/{_calibracion.Id}", new { numeroCertificado = "CERT-7" });
        Assert.Equal(HttpStatusCode.OK, corregida.StatusCode);
        Assert.Equal("CERT-7", _calibracion.NumeroCertificado);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PatchAsync($"{Calibraciones}/{_calibracion.Id}/aprobacion", null)).StatusCode);
        Assert.False(_calibracion.Aprobada);
    }

    [Fact]
    public async Task EquipoQueNoRequiereCalibracion_Responde409_YSinPatron400()
    {
        var cliente = Cliente(CodigosRol.TecnicoMantenimiento);

        var conflicto = await cliente.PostAsJsonAsync(Calibraciones, new { equipoId = _estetoscopio.Id, fechaCalibracion = Hoy, patronReferencia = "Analizador" });
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Contains(GestionCalibraciones.CodigoNoRequiere, await conflicto.Content.ReadAsStringAsync());

        var invalida = await cliente.PostAsJsonAsync(Calibraciones, new { equipoId = _equipo.Id, fechaCalibracion = Hoy });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Contains("patronReferencia", await invalida.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ingeniero_Aprueba_PeroNoRegistra()
    {
        var cliente = Cliente(CodigosRol.IngenieroBiomedico);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync(Calibraciones, new { equipoId = _equipo.Id, fechaCalibracion = Hoy, patronReferencia = "Analizador" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PatchAsJsonAsync($"{Calibraciones}/{_calibracion.Id}", new { numeroCertificado = "X" })).StatusCode);

        var aprobada = await cliente.PatchAsync($"{Calibraciones}/{_calibracion.Id}/aprobacion", null);
        Assert.Equal(HttpStatusCode.OK, aprobada.StatusCode);
        Assert.True(_calibracion.Aprobada);

        var otraVez = await cliente.PatchAsync($"{Calibraciones}/{_calibracion.Id}/aprobacion", null);
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task RolesDeMantenimientos_ConsultanLasCalibraciones(string rol)
    {
        var cliente = Cliente(rol);

        var pagina = await cliente.GetFromJsonAsync<JsonElement>($"{Calibraciones}?equipoId={_equipo.Id}");
        Assert.Equal(1, pagina.GetProperty("total").GetInt32());
        var item = pagina.GetProperty("items")[0];
        Assert.Equal(("MON-07", "conforme", true, false), (item.GetProperty("placaInventario").GetString(), item.GetProperty("resultado").GetString(), item.GetProperty("vigente").GetBoolean(), item.GetProperty("aprobada").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"{Calibraciones}/{_calibracion.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task RolesDeConsulta_NoRegistranNiAprueban(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync(Calibraciones, new { equipoId = _equipo.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PatchAsync($"{Calibraciones}/{_calibracion.Id}/aprobacion", null)).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(null)]
    public async Task SinPermiso_NoConsulta(string? rol)
    {
        var esperado = rol is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;

        Assert.Equal(esperado, (await Cliente(rol).GetAsync(Calibraciones)).StatusCode);
    }
}
