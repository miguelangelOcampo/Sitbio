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
/// Contrato HTTP de /api/v1/tiquetes (API-TIQ; HU-003, HU-017, HU-018): reportan AC-02 y AC-04;
/// consultan AC-01 a AC-04 y AC-07 (AC-04 solo lo suyo); los demás roles reciben 403. Sin base de datos.
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
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task QuienHaceSeguimiento_Consulta(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}?estado=recibido&criticidad=critica");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var soloPropios = rol == CodigosRol.PersonalClinico ? _usuario.Id : (Guid?)null;
        await _repositorio.Received(1).ListarAsync(
            new FiltroTiquetes(EstadosTiquete.Recibido, Criticidades.Critica, 1, 20), soloPropios, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    public async Task OtrosRoles_NoConsultan(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/TQT-000001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/TQT-000001/historial")).StatusCode);
    }

    [Fact]
    public async Task Mios_ListaSoloLosQueReportoQuienConsulta()
    {
        var respuesta = await Cliente(CodigosRol.IngenieroBiomedico).GetAsync($"{Ruta}?mios=true");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await _repositorio.Received(1).ListarAsync(new FiltroTiquetes(null, null, 1, 20, Mios: true), _usuario.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdenCriticidad_PideLaBandejaPriorizada_YOtroOrdenResponde400()
    {
        var cliente = Cliente(CodigosRol.IngenieroBiomedico);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"{Ruta}?estado=recibido&orden=Criticidad")).StatusCode);
        await _repositorio.Received(1).ListarAsync(
            new FiltroTiquetes(EstadosTiquete.Recibido, null, 1, 20, PorCriticidad: true), null, Arg.Any<CancellationToken>());

        var invalido = await cliente.GetAsync($"{Ruta}?orden=fecha");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        using var problema = JsonDocument.Parse(await invalido.Content.ReadAsStringAsync());
        Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("orden", out _));
    }

    [Fact]
    public async Task Historial_PorCodigo_TraeLosCambiosYSinTokenResponde401()
    {
        var cliente = Cliente(CodigosRol.PersonalClinico);
        await cliente.PostAsJsonAsync(Ruta, Cuerpo());
        _repositorio.HistorialAsync(_guardado!.Id, Arg.Any<CancellationToken>()).Returns([new HistorialTiqueteLeido(_guardado.Recepcion(), "Laura Gómez")]);

        var respuesta = await cliente.GetAsync($"{Ruta}/tqt-000001/historial");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var historial = (await respuesta.Content.ReadFromJsonAsync<HistorialTiqueteDto[]>(Json))!;
        Assert.Equal([(null, EstadosTiquete.Recibido, "Laura Gómez")], historial.Select(h => (h.EstadoAnterior, h.NuevoEstado, h.Usuario)));
        await _repositorio.Received().ObtenerAsync("TQT-000001", _usuario.Id, Arg.Any<CancellationToken>());
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/TQT-000777/historial")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null).GetAsync($"{Ruta}/TQT-000001/historial")).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.PersonalClinico, HttpStatusCode.OK)]
    [InlineData(CodigosRol.IngenieroBiomedico, HttpStatusCode.OK)]
    [InlineData(CodigosRol.TecnicoMantenimiento, HttpStatusCode.Forbidden)]
    [InlineData(CodigosRol.AdministradorSistema, HttpStatusCode.Forbidden)]
    public async Task TiposDeFalla_SoloParaQuienReporta(string rol, HttpStatusCode esperado)
    {
        var cliente = Cliente(rol);
        _repositorio.TiposFallaActivosAsync(Arg.Any<CancellationToken>()).Returns(["Alarma", "No enciende"]);

        var respuesta = await cliente.GetAsync($"{Ruta}/tipos-falla");

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.OK)
        {
            Assert.Equal(["Alarma", "No enciende"], (await respuesta.Content.ReadFromJsonAsync<string[]>(Json))!);
        }
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
