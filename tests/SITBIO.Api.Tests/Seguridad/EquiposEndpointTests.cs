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
using Sitbio.Application.Equipos;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de /api/v1/equipos (API-EQ-LIST y API-EQ): consulta para AC-01 a AC-05 y AC-07,
/// registro y edición solo para AC-02 (RNF-003). Sin base de datos.
/// </summary>
public class EquiposEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/equipos";
    private readonly IRepositorioEquipos _repositorio = Substitute.For<IRepositorioEquipos>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();

    private HttpClient Cliente(string? codigoRol)
    {
        _repositorio.ListarAsync(default!, default).ReturnsForAnyArgs((Array.Empty<EquipoLeido>(), 0));
        _repositorio.CapacidadEquiposAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadEquipos("Clínica", 1000, 0));
        _repositorio.DuplicadosAsync(default!, default!, default, default).ReturnsForAnyArgs(new Duplicados(false, false));
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

    private static object Cuerpo(Guid tipo, Guid ubicacion) => new
    {
        tipoEquipoId = tipo,
        ubicacionId = ubicacion,
        numeroSerie = "SN-1",
        placaInventario = "PL-1",
        marca = "Mindray",
        modelo = "T5",
        tipoAutorizacion = TiposAutorizacion.RegistroSanitario,
        registroInvima = "INVIMA 2020DM-1",
        venceRegistro = "2028-01-31",
        claseRiesgo = ClasesRiesgo.IIb,
        requiereCalibracion = true,
        periodicidadCalibracionMeses = 12,
    };

    [Fact]
    public async Task SinToken_Responde401()
    {
        var respuesta = await Cliente(null).GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-07")]
    public async Task RolesDeConsulta_ListanEquipos(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}?clase=IIb&calibracion=true");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await _repositorio.Received(1).ListarAsync(
            Arg.Is<FiltroEquipos>(f => f.Clase == "IIb" && f.Calibracion == true && f.Pagina == 1 && f.Tamano == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Directiva_NoConsultaElInventario()
    {
        var respuesta = await Cliente("AC-06").GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-03")]
    [InlineData("AC-05")]
    [InlineData("AC-07")]
    public async Task SoloElIngenieroBiomedico_RegistraYEdita(string rol)
    {
        var cliente = Cliente(rol);

        var respuestas = new[]
        {
            await cliente.PostAsJsonAsync(Ruta, Cuerpo(Guid.NewGuid(), Guid.NewGuid())),
            await cliente.PutAsJsonAsync($"{Ruta}/{Guid.NewGuid()}", Cuerpo(Guid.NewGuid(), Guid.NewGuid())),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
        await _bitacora.Received(2).RegistrarAsync(
            Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoDenegado && e.Entidad == "equipos"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngenieroBiomedico_RegistraEquipo201()
    {
        var tipo = new TipoEquipo(Guid.NewGuid(), "Monitor");
        var ubicacion = new ElementoCatalogo(Guid.NewGuid(), "UCI", true);
        _repositorio.TipoEquipoAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        _repositorio.UbicacionAsync(ubicacion.Id, Arg.Any<CancellationToken>()).Returns(ubicacion);

        var respuesta = await Cliente("AC-02").PostAsJsonAsync(Ruta, Cuerpo(tipo.Id, ubicacion.Id));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("OPERATIVO", cuerpo.RootElement.GetProperty("estado").GetString());
        Assert.Equal("2028-01-31", cuerpo.RootElement.GetProperty("venceRegistro").GetString());
        Assert.EndsWith($"/equipos/{cuerpo.RootElement.GetProperty("id").GetString()}", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task IngenieroBiomedico_CupoLleno_Responde409ConCodigoRn009()
    {
        var cliente = Cliente("AC-02");
        _repositorio.CapacidadEquiposAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadEquipos("Consultorio", 300, 300));

        var respuesta = await cliente.PostAsJsonAsync(Ruta, Cuerpo(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(GestionEquipos.CodigoLimitePlan, cuerpo.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task EquipoDeOtroTenant_Responde404()
    {
        var respuesta = await Cliente("AC-03").GetAsync($"{Ruta}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // ---------- HU-014: estado, baja lógica, reversión e historial ----------

    private EquipoBiomedico EquipoGuardado()
    {
        var equipo = new EquipoBiomedico(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SN-9", "PL-9", "Mindray", "T5", DateTimeOffset.UtcNow);
        _repositorio.ObtenerAsync(equipo.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => new EquipoLeido(equipo, "Monitor", "UCI", null, EstadosEquipo.Codigos[equipo.EstadoEquipoId - 1], "Estado", null));
        return equipo;
    }

    [Fact]
    public async Task CambiarEstado_SinToken_Responde401()
    {
        var respuesta = await Cliente(null).PatchAsJsonAsync($"{Ruta}/{Guid.NewGuid()}/estado", new { estado = "BAJA", motivo = "Obsoleto" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-03")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-07")]
    public async Task SoloElIngenieroBiomedico_CambiaEstadoYDaDeBaja(string rol)
    {
        var respuesta = await Cliente(rol).PatchAsJsonAsync($"{Ruta}/{Guid.NewGuid()}/estado", new { estado = "BAJA", motivo = "Obsoleto" });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task IngenieroBiomedico_DaDeBaja200ConElEquipoActualizado()
    {
        var cliente = Cliente("AC-02");
        var equipo = EquipoGuardado();

        var respuesta = await cliente.PatchAsJsonAsync($"{Ruta}/{equipo.Id}/estado", new { estado = "BAJA", motivo = "Obsoleto, sin repuestos." });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("BAJA", cuerpo.RootElement.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.String, cuerpo.RootElement.GetProperty("fechaBaja").ValueKind);
        _repositorio.Received(1).Agregar(Arg.Is<HistorialEstadoEquipo>(h => h.NuevoEstadoId == EstadosEquipo.Baja));
    }

    [Fact]
    public async Task CambiarEstado_SinMotivo_Responde400EnMotivo()
    {
        var cliente = Cliente("AC-02");
        var equipo = EquipoGuardado();

        var respuesta = await cliente.PatchAsJsonAsync($"{Ruta}/{equipo.Id}/estado", new { estado = "FUERA_DE_SERVICIO", motivo = " " });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.True(cuerpo.RootElement.GetProperty("errors").TryGetProperty("motivo", out _));
    }

    [Fact]
    public async Task CambiarEstado_AlMismoEstado_Responde409Eq001()
    {
        var cliente = Cliente("AC-02");
        var equipo = EquipoGuardado();

        var respuesta = await cliente.PatchAsJsonAsync($"{Ruta}/{equipo.Id}/estado", new { estado = "OPERATIVO", motivo = "Revisión" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(GestionEquipos.CodigoMismoEstado, cuerpo.RootElement.GetProperty("codigo").GetString());
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-07")]
    public async Task RolesDeConsulta_VenElHistorialDeEstados(string rol)
    {
        var cliente = Cliente(rol);
        var equipo = EquipoGuardado();
        var baja = equipo.DarDeBaja("Obsoleto", Guid.NewGuid(), DateTimeOffset.UtcNow);
        _repositorio.HistorialAsync(equipo.Id, Arg.Any<CancellationToken>())
            .Returns([new CambioEstadoLeido(baja, "OPERATIVO", "Operativo", "BAJA", "Baja", "Ana Gómez")]);

        var respuesta = await cliente.GetAsync($"{Ruta}/{equipo.Id}/estados");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var fila = Assert.Single(cuerpo.RootElement.EnumerateArray());
        Assert.Equal("BAJA", fila.GetProperty("estado").GetString());
        Assert.Equal("Obsoleto", fila.GetProperty("motivo").GetString());
        Assert.Equal("Ana Gómez", fila.GetProperty("usuario").GetString());
    }
}
