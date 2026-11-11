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
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de /api/v1/catalogos (API-CAT, HU-025): AC-01 y AC-02 consultan; AC-02 gestiona los
/// cuatro catálogos y AC-01 solo las ubicaciones; los demás roles reciben 403 (RNF-003). Sin base de datos.
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

    // ---------- HU-025: POST, PUT y GET por id ----------

    [Fact]
    public async Task Crear_SinToken_Responde401()
    {
        var respuesta = await Cliente(null).PostAsJsonAsync($"{Ruta}/tipos-falla", new { nombre = "No enciende" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData("AC-07")]
    public async Task Crear_OtrosRoles_Responde403(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync($"{Ruta}/ubicaciones", new { nombre = "Urgencias" });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        _repositorio.DidNotReceiveWithAnyArgs().Agregar(default!);
    }

    [Fact]
    public async Task CA_HU_025_01_Ingeniero_CreaUnTipoDeFalla_201ConLocation()
    {
        var respuesta = await Cliente("AC-02").PostAsJsonAsync($"{Ruta}/Tipos-Falla", new { nombre = " No enciende " });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<CatalogoDto>();
        Assert.Equal(("No enciende", true), (creado!.Nombre, creado.Activo));
        Assert.Equal($"{Ruta}/tipos-falla/{creado.Id}", respuesta.Headers.Location!.OriginalString);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CA_HU_025_03_Administrador_CreaUbicacionPeroNoProveedores()
    {
        var cliente = Cliente("AC-01");

        var ubicacion = await cliente.PostAsJsonAsync($"{Ruta}/ubicaciones", new { nombre = "Rack R-07", descripcion = "Cuarto de cableado" });
        var proveedor = await cliente.PostAsJsonAsync($"{Ruta}/proveedores", new { nombre = "Equimédica", nit = "800.111.222-3", tipoProveedor = "Distribuidor" });

        Assert.Equal(HttpStatusCode.Created, ubicacion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proveedor.StatusCode);
        Assert.Contains("Ingeniero Biomédico", await proveedor.Content.ReadAsStringAsync());
        await _bitacora.Received(1).RegistrarAsync(
            Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoDenegado), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_SinNombre_Responde400PorCampo()
    {
        var respuesta = await Cliente("AC-02").PostAsJsonAsync($"{Ruta}/ubicaciones", new { nombre = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.True(cuerpo.RootElement.GetProperty("errors").TryGetProperty("nombre", out _));
    }

    [Fact]
    public async Task Crear_NombreRepetido_Responde409ConCodigo()
    {
        _repositorio.NombreEnUsoAsync(TiposCatalogo.Ubicaciones, "Urgencias", null, Arg.Any<CancellationToken>()).Returns(true);

        var respuesta = await Cliente("AC-02").PostAsJsonAsync($"{Ruta}/ubicaciones", new { nombre = "Urgencias" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(ErroresCatalogo.CodigoDuplicado, cuerpo.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task CA_HU_025_02_Editar_InactivaElElemento()
    {
        var falla = new TipoFalla(Guid.NewGuid(), "Eléctrica");
        _repositorio.ObtenerAsync(TiposCatalogo.TiposFalla, falla.Id, Arg.Any<CancellationToken>()).Returns(falla);

        var respuesta = await Cliente("AC-02").PutAsJsonAsync($"{Ruta}/tipos-falla/{falla.Id}", new { nombre = "Eléctrica", activo = false });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var editado = await respuesta.Content.ReadFromJsonAsync<CatalogoDto>();
        Assert.False(editado!.Activo);
    }

    [Fact]
    public async Task Editar_ElementoInexistenteODeOtraInstitucion_Responde404()
    {
        _repositorio.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IElementoCatalogo?)null);

        var respuesta = await Cliente("AC-02").PutAsJsonAsync($"{Ruta}/ubicaciones/{Guid.NewGuid()}", new { nombre = "Urgencias" });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-02")]
    public async Task Detalle_DelProveedor_TraeSuContacto(string rol)
    {
        var proveedor = new Proveedor(Guid.NewGuid(), "CalibraLab", "901.555.222-1", "Laboratorio de calibración", new ContactoProveedor("Mesa de ayuda", null, "604 444 5555"));
        _repositorio.ObtenerAsync(TiposCatalogo.Proveedores, proveedor.Id, Arg.Any<CancellationToken>()).Returns(proveedor);

        var detalle = await Cliente(rol).GetFromJsonAsync<CatalogoDto>($"{Ruta}/proveedores/{proveedor.Id}");

        Assert.Equal(("CalibraLab", "Mesa de ayuda", "604 444 5555"), (detalle!.Nombre, detalle.NombreContacto, detalle.Telefono));
    }
}
