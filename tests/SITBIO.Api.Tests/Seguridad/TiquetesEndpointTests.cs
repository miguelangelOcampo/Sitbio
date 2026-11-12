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
using Sitbio.Application.Seguridad;
using Sitbio.Application.Tiquetes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de /api/v1/tiquetes (API-TIQ; HU-003, HU-018): reportan AC-02 y AC-04; consultan
/// AC-02, AC-03 y AC-04 (AC-04 solo lo suyo); los demás roles reciben 403. Sin base de datos.
/// </summary>
public class TiquetesEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/tiquetes";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;
    private readonly IRepositorioTiquetes _repositorio = Substitute.For<IRepositorioTiquetes>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly EquipoBiomedico _monitor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SN-1", "PL-001", "Mindray", "uMEC10", Ahora);
    private Tiquete? _guardado;

    private TiqueteLeido Leido(Tiquete tiquete) =>
        new(tiquete, _monitor.PlacaInventario, _monitor.NumeroSerie, "Monitor", "Urgencias", "Laura Gómez", EstadosEquipo.Codigos[_monitor.EstadoEquipoId - 1]);

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        _repositorio.EquipoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EquipoBiomedico?)null);
        _repositorio.EquipoAsync(_monitor.Id, Arg.Any<CancellationToken>()).Returns(_monitor);
        _repositorio.TipoFallaActivoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(llamada => llamada.ArgAt<string>(0));
        _repositorio.RegistrarAsync(Arg.Any<Tiquete>(), Arg.Any<HistorialTiquete>(), Arg.Any<HistorialEstadoEquipo?>(), Arg.Any<CancellationToken>())
            .Returns(llamada =>
            {
                _guardado = llamada.ArgAt<Tiquete>(0);
                _guardado.Numerar(1);
                return Task.CompletedTask;
            });
        _repositorio.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(llamada => _guardado is { } t && t.Codigo == llamada.ArgAt<string>(0) ? Leido(t) : null);
        _repositorio.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(([], 0));

        var cliente = fabrica.WithWebHostBuilder(b => b
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

    private object Cuerpo(string criticidad = Criticidades.Alta) =>
        new { equipoId = _monitor.Id, tipoFalla = "No enciende", descripcion = "La pantalla queda en negro.", criticidad };

    [Fact]
    public async Task SinToken_Responde401()
    {
        var cliente = Cliente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync(Ruta, Cuerpo())).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    public async Task PersonalClinicoEIngeniero_Reportan_Y_RecibenElCodigo(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync(Ruta, Cuerpo());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal($"{Ruta}/TQT-000001", respuesta.Headers.Location!.OriginalString);
        var creado = await respuesta.Content.ReadFromJsonAsync<TiqueteDto>(Json);
        Assert.Equal(("TQT-000001", EstadosTiquete.Recibido, _usuario.Id), (creado!.Codigo, creado.Estado, creado.ReportanteId));
        Assert.Equal(Criticidades.Alta, creado.PrioridadSugerida);
    }

    [Fact]
    public async Task ReporteCritico_DevuelveElEquipoFueraDeServicio()
    {
        var respuesta = await Cliente(CodigosRol.PersonalClinico).PostAsJsonAsync(Ruta, Cuerpo(Criticidades.Critica));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<TiqueteDto>(Json);
        Assert.True(creado!.EquipoFueraDeServicio);
        await _repositorio.Received(1).RegistrarAsync(
            Arg.Any<Tiquete>(), Arg.Any<HistorialTiquete>(), Arg.Is<HistorialEstadoEquipo?>(h => h != null && h.NuevoEstadoId == EstadosEquipo.FueraDeServicio), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task OtrosRoles_NoReportan(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync(Ruta, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task SinEquipo_Responde400EnEspanolYNoGuarda()
    {
        var respuesta = await Cliente(CodigosRol.PersonalClinico).PostAsJsonAsync(Ruta,
            new { tipoFalla = "No enciende", descripcion = "Falla", criticidad = Criticidades.Baja });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("Selecciona el equipo que presenta la falla.", problema.RootElement.GetProperty("errors").GetProperty("equipoId")[0].GetString());
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task EquipoDeOtraInstitucion_Responde404()
    {
        var respuesta = await Cliente(CodigosRol.PersonalClinico).PostAsJsonAsync(Ruta,
            new { equipoId = Guid.NewGuid(), tipoFalla = "No enciende", descripcion = "Falla", criticidad = Criticidades.Baja });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    public async Task IngenieroTecnicoYClinico_Consultan(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}?estado=recibido&criticidad=critica");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var soloPropios = rol == CodigosRol.PersonalClinico ? _usuario.Id : (Guid?)null;
        await _repositorio.Received(1).ListarAsync(
            new FiltroTiquetes(EstadosTiquete.Recibido, Criticidades.Critica, 1, 20), soloPropios, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task OtrosRoles_NoConsultan(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/TQT-000001")).StatusCode);
    }

    [Fact]
    public async Task ObtenerPorCodigo_PersonalClinicoSoloLoSuyo()
    {
        var cliente = Cliente(CodigosRol.PersonalClinico);
        await cliente.PostAsJsonAsync(Ruta, Cuerpo());

        var respuesta = await cliente.GetAsync($"{Ruta}/tqt-000001");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await _repositorio.Received().ObtenerAsync("TQT-000001", _usuario.Id, Arg.Any<CancellationToken>());
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/TQT-000777")).StatusCode);
    }
}
