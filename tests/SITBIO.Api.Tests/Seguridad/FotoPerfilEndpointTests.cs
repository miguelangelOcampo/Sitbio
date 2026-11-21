using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Equipos;
using Sitbio.Application.Seguridad;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// J-11: contrato de /api/v1/usuarios/me/foto. Cualquier rol con token cambia, consulta y quita su propia foto
/// (el usuario sale del token); sin token, 401. Sin base de datos.
/// </summary>
public class FotoPerfilEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/usuarios/me/foto";
    private readonly IRepositorioUsuarios _usuarios = Substitute.For<IRepositorioUsuarios>();
    private readonly IAlmacenDocumentos _almacen = Substitute.For<IAlmacenDocumentos>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 4, "Laura", "Gómez", "laura@demo.co", "hash");

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        _usuarios.ObtenerAsync(_usuario.Id, Arg.Any<CancellationToken>()).Returns((_usuario, codigoRol ?? CodigosRol.PersonalClinico));
        _almacen.GuardarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => $"{c.ArgAt<Guid>(0)}/{c.ArgAt<Guid>(1)}/{c.ArgAt<string>(2)}");
        var cliente = fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _usuarios)
                    .AddScoped(_ => _almacen)
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

    private static byte[] Png(int ancho, int alto)
    {
        byte[] d = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0, 0];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), ancho);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(20), alto);
        return d;
    }

    private static MultipartFormDataContent Formulario(int ladoMiniatura = 128)
    {
        var formulario = new MultipartFormDataContent();
        foreach (var (campo, datos) in new[] { ("miniatura", Png(ladoMiniatura, ladoMiniatura)), ("vista", Png(512, 400)) })
        {
            var parte = new ByteArrayContent(datos);
            parte.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            formulario.Add(parte, campo, $"{campo}.png");
        }

        return formulario;
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task CualquierRol_CambiaConsultaYQuitaSuPropiaFoto(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PutAsync(Ruta, Formulario())).StatusCode);
        Assert.NotNull(_usuario.FotoRuta);
        await _almacen.Received(2).GuardarAsync(_usuario.TenantId, _usuario.Id, Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        _almacen.Abrir(Arg.Any<string>()).Returns(_ => new MemoryStream(Png(128, 128)));

        var foto = await cliente.GetAsync($"{Ruta}?tam=128");
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/png", foto.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync(Ruta)).StatusCode);
        Assert.Null(_usuario.FotoRuta);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(Ruta)).StatusCode);
    }

    [Fact]
    public async Task SinToken_Responde401()
    {
        var cliente = Cliente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PutAsync(Ruta, Formulario())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.DeleteAsync(Ruta)).StatusCode);
        await _usuarios.DidNotReceiveWithAnyArgs().ObtenerAsync(default, default);
    }

    [Fact]
    public async Task MiniaturaDeOtroTamano_Responde400()
    {
        var respuesta = await Cliente(CodigosRol.PersonalClinico).PutAsync(Ruta, Formulario(ladoMiniatura: 100));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Null(_usuario.FotoRuta);
    }

    [Fact]
    public async Task SoloSeConsultaElUsuarioDelToken()
    {
        await Cliente(CodigosRol.IngenieroBiomedico).PutAsync(Ruta, Formulario());

        await _usuarios.Received().ObtenerAsync(_usuario.Id, Arg.Any<CancellationToken>());
        await _usuarios.DidNotReceive().ObtenerAsync(Arg.Is<Guid>(id => id != _usuario.Id), Arg.Any<CancellationToken>());
    }
}
