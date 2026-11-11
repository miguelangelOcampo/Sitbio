using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de API-AUTH-PASSWORD y cambio obligatorio de la clave temporal (HU-013,
/// CA-HU-013-02): mientras la clave sea temporal, el resto de la API responde 403 con MSG-004.
/// </summary>
public class CambioClaveEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private const string Temporal = "Tmp#7kPq2xWz9a";
    private const string Nueva = "Monitor#2026uci";
    private static readonly string Password = $"{RutasApi.V1}/auth/password";

    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IHasherContrasenas _hasher = Substitute.For<IHasherContrasenas>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 3, "Ana", "Pérez", "ana.perez@demo-a.co", "hash-temporal");

    private (HttpClient Cliente, TokenEmitido Token) Cliente(bool conToken = true)
    {
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _sesiones)
                    .AddScoped(_ => _bitacora)
                    .AddSingleton(_hasher)))
            .CreateClient();

        var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
        var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, "AC-03");
        if (conToken)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        return (cliente, token);
    }

    /// <summary>La sesión está vigente y su estado sigue a la marca de clave temporal del usuario, como en la base.</summary>
    private SesionToken ConClaveTemporal(TokenEmitido token)
    {
        _usuario.AsignarClaveTemporal("hash-temporal");
        _hasher.Verificar(Temporal, "hash-temporal").Returns(true);
        _hasher.Hash(Nueva).Returns("hash-nuevo");

        var sesion = new SesionToken(_usuario.TenantId, _usuario.Id, token.Jti, token.Emision, token.Expiracion);
        _sesiones.ConsultarEstadoAsync(_usuario.TenantId, token.Jti, Arg.Any<CancellationToken>()).Returns(_ =>
            sesion.EstaRevocada ? EstadoSesion.Cerrada
            : _usuario.CambioContrasenaObligatorio ? EstadoSesion.ClaveTemporal
            : EstadoSesion.Activa);
        _sesiones.ObtenerAsync(token.Jti, Arg.Any<CancellationToken>()).Returns(sesion);
        _sesiones.BuscarUsuarioAsync(_usuario.Id, Arg.Any<CancellationToken>()).Returns(new UsuarioParaAcceso(_usuario, "AC-03", true));
        _sesiones.VigentesDelUsuarioAsync(_usuario.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([sesion]);
        return sesion;
    }

    private static async Task<string?> Codigo(HttpResponseMessage respuesta)
    {
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return cuerpo.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
    }

    [Fact]
    public async Task SinToken_Responde401()
    {
        var (cliente, _) = Cliente(conToken: false);

        var respuesta = await cliente.PutAsJsonAsync(Password, new { actual = Temporal, nueva = Nueva });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CA_HU_013_02_ConClaveTemporal_ElRestoDeLaApiResponde403ConMsg004()
    {
        var (cliente, token) = Cliente();
        ConClaveTemporal(token);

        var respuesta = await cliente.GetAsync($"{RutasApi.V1}/equipos");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(ClaveTemporal.Codigo, await Codigo(respuesta));
        await _bitacora.Received(1).RegistrarAsync(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoDenegado), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CA_HU_013_02_ConClaveTemporal_PuedeCerrarSesion()
    {
        var (cliente, token) = Cliente();
        var sesion = ConClaveTemporal(token);

        var respuesta = await cliente.PostAsync($"{RutasApi.V1}/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.True(sesion.EstaRevocada);
    }

    [Fact]
    public async Task CA_HU_013_02_TrasCambiarla_ElMismoTokenSirveParaElRestoDeLaApi()
    {
        var (cliente, token) = Cliente();
        var sesion = ConClaveTemporal(token);

        var cambio = await cliente.PutAsJsonAsync(Password, new { actual = Temporal, nueva = Nueva });
        // El Técnico no consulta la bitácora: ahora el 403 es por su rol, ya no por la clave temporal.
        var despues = await cliente.GetAsync($"{RutasApi.V1}/bitacora");

        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
        Assert.Equal("hash-nuevo", _usuario.ContrasenaHash);
        Assert.False(sesion.EstaRevocada);
        Assert.Equal(HttpStatusCode.Forbidden, despues.StatusCode);
        Assert.Null(await Codigo(despues));
    }

    [Fact]
    public async Task NuevaQueNoCumpleLaPolitica_Responde400ConLasReglas()
    {
        var (cliente, token) = Cliente();
        ConClaveTemporal(token);

        var respuesta = await cliente.PutAsJsonAsync(Password, new { actual = Temporal, nueva = "corta" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var reglas = cuerpo.RootElement.GetProperty("errors").GetProperty("nueva").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains(PoliticaContrasena.ReglaLongitud, reglas);
        Assert.True(_usuario.CambioContrasenaObligatorio);
    }
}
