using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>Contrato HTTP de /api/v1/usuarios: permiso solo para AC-01 (RN-002), sin base de datos.</summary>
public class UsuariosEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/usuarios";
    private readonly IRepositorioUsuarios _repositorio = Substitute.For<IRepositorioUsuarios>();

    private HttpClient Cliente(string? codigoRol)
    {
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s.AddScoped(_ => _repositorio)))
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

    [Theory]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    [InlineData("AC-07")]
    public async Task OtroRol_Responde403EnTodasLasOperaciones(string rol)
    {
        var cliente = Cliente(rol);
        var id = Guid.NewGuid();

        var respuestas = new[]
        {
            await cliente.GetAsync(Ruta),
            await cliente.PostAsJsonAsync(Ruta, new { nombres = "A", apellidos = "B", correo = "a@b.co", rol = "AC-03" }),
            await cliente.PatchAsJsonAsync($"{Ruta}/{id}/estado", new { accion = "desactivar" }),
            await cliente.PostAsync($"{Ruta}/{id}/clave-temporal", null),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task Administrador_ListaUsuariosPaginados()
    {
        _repositorio.ListarAsync(1, 20, Arg.Any<CancellationToken>()).Returns((Array.Empty<(Usuario, string)>(), 0));

        var respuesta = await Cliente("AC-01").GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(0, cuerpo.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(20, cuerpo.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Administrador_CreaUsuarioConClaveTemporalYSinHash()
    {
        _repositorio.RolPorCodigoAsync("AC-03", Arg.Any<CancellationToken>()).Returns(new RolAsignable(3, "AC-03"));

        var respuesta = await Cliente("AC-01").PostAsJsonAsync(Ruta,
            new { nombres = "Laura", apellidos = "Gómez", correo = "laura@demo.co", telefono = (string?)null, rol = "AC-03" });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        using var cuerpo = JsonDocument.Parse(texto);
        Assert.Equal(GeneradorClaves.Longitud, cuerpo.RootElement.GetProperty("claveTemporal").GetString()!.Length);
        Assert.Equal("AC-03", cuerpo.RootElement.GetProperty("usuario").GetProperty("rol").GetString());
        Assert.True(cuerpo.RootElement.GetProperty("usuario").GetProperty("debeCambiarClave").GetBoolean());
        Assert.DoesNotContain("hash", texto, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith($"/usuarios/{cuerpo.RootElement.GetProperty("usuario").GetProperty("id").GetString()}", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Administrador_CorreoEnUso_Responde409ConCodigo()
    {
        _repositorio.RolPorCodigoAsync("AC-03", Arg.Any<CancellationToken>()).Returns(new RolAsignable(3, "AC-03"));
        _repositorio.CorreoEnUsoAsync("laura@demo.co", null, Arg.Any<CancellationToken>()).Returns(true);

        var respuesta = await Cliente("AC-01").PostAsJsonAsync(Ruta,
            new { nombres = "Laura", apellidos = "Gómez", correo = "laura@demo.co", rol = "AC-03" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(GestionUsuarios.CodigoCorreoEnUso, cuerpo.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Administrador_UsuarioDeOtroTenant_Responde404()
    {
        var respuesta = await Cliente("AC-01").GetAsync($"{Ruta}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
