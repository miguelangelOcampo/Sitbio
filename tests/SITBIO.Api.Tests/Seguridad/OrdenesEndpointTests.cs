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
using Sitbio.Application.Ordenes;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de /api/v1/ordenes (API-OT; HU-004): solo AC-02 asigna; consultan AC-01, AC-02, AC-03
/// y AC-07 (AC-03 solo las suyas); los demás roles reciben 403. Sin base de datos.
/// </summary>
public class OrdenesEndpointTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/ordenes";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IRepositorioOrdenes _repositorio = Substitute.For<IRepositorioOrdenes>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Andrés", "Mejía", "andres@demo.co", "hash");
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Tiquete _tiquete = Recibido();
    private OrdenTrabajo? _guardada;

    private static Tiquete Recibido()
    {
        var tiquete = new Tiquete(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, Criticidades.Alta, DateTimeOffset.UtcNow);
        tiquete.Numerar(1);
        return tiquete;
    }

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        _repositorio.TiqueteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Tiquete?)null);
        _repositorio.TiqueteAsync(_tiquete.Id, Arg.Any<CancellationToken>()).Returns(_tiquete);
        _repositorio.TecnicoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(llamada => new TecnicoCandidato(llamada.ArgAt<Guid>(0), CodigosRol.PersonalClinico, true));
        _repositorio.TecnicoAsync(_tecnico, Arg.Any<CancellationToken>()).Returns(new TecnicoCandidato(_tecnico, CodigosRol.TecnicoMantenimiento, true));
        _repositorio.RegistrarAsync(Arg.Any<OrdenTrabajo>(), Arg.Any<HistorialTiquete>(), Arg.Any<CancellationToken>())
            .Returns(llamada =>
            {
                _guardada = llamada.ArgAt<OrdenTrabajo>(0);
                return Task.CompletedTask;
            });
        _repositorio.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(llamada => _guardada is { } o && o.Id == llamada.ArgAt<Guid>(0)
                ? new OrdenLeida(o, "PL-001", "SN-1", "Monitor", "Urgencias", "Carlos Ruiz", _tiquete.Codigo, _tiquete.TipoFalla)
                : null);
        _repositorio.ListarAsync(Arg.Any<FiltroOrdenes>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(([], 0));

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

    private object Cuerpo(Guid? tecnico = null) => new { tiqueteId = _tiquete.Id, tecnicoId = tecnico ?? _tecnico };

    [Fact]
    public async Task SinToken_Responde401()
    {
        var cliente = Cliente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync(Ruta, Cuerpo())).StatusCode);
    }

    [Fact]
    public async Task Ingeniero_Asigna_Y_RecibeLaOrdenConElTiqueteAsignado()
    {
        var respuesta = await Cliente(CodigosRol.IngenieroBiomedico).PostAsJsonAsync(Ruta, Cuerpo());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creada = await respuesta.Content.ReadFromJsonAsync<OrdenDto>(Json);
        Assert.Equal($"{Ruta}/{creada!.Id}", respuesta.Headers.Location!.OriginalString);
        Assert.Equal((_tecnico, (Guid?)_usuario.Id, EstadosOrden.Asignada), (creada.TecnicoId, creada.AsignadoPorId, creada.Estado));
        Assert.Equal(("TQT-000001", Criticidades.Alta), (creada.CodigoTiquete, creada.Prioridad));
        Assert.Equal(EstadosTiquete.Asignado, _tiquete.Estado);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task OtrosRoles_NoAsignan(string rol)
    {
        var respuesta = await Cliente(rol).PostAsJsonAsync(Ruta, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        await _repositorio.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default!, default);
    }

    [Fact]
    public async Task AQuienNoEsTecnico_Responde400ConRn003()
    {
        var respuesta = await Cliente(CodigosRol.IngenieroBiomedico).PostAsJsonAsync(Ruta, Cuerpo(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(GestionOrdenes.MensajeRn003, problema.RootElement.GetProperty("errors").GetProperty("tecnicoId")[0].GetString());
        Assert.Equal(EstadosTiquete.Recibido, _tiquete.Estado);
    }

    [Fact]
    public async Task TiqueteYaAsignado_Responde409ConCodigo()
    {
        var cliente = Cliente(CodigosRol.IngenieroBiomedico);
        await cliente.PostAsJsonAsync(Ruta, Cuerpo());

        var respuesta = await cliente.PostAsJsonAsync(Ruta, Cuerpo());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(GestionOrdenes.CodigoTiqueteAsignado, problema.RootElement.GetProperty("codigo").GetString());
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task QuienSigueLasOrdenes_Consulta(string rol)
    {
        var respuesta = await Cliente(rol).GetAsync($"{Ruta}?estado=asignada");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var soloSuyas = rol == CodigosRol.TecnicoMantenimiento ? _usuario.Id : (Guid?)null;
        await _repositorio.Received(1).ListarAsync(new FiltroOrdenes(EstadosOrden.Asignada), soloSuyas, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    public async Task OtrosRoles_NoConsultan(string rol)
    {
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task AsignadasMe_ListaLasDelUsuarioDelToken()
    {
        var respuesta = await Cliente(CodigosRol.IngenieroBiomedico).GetAsync($"{Ruta}?asignadas=me");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await _repositorio.Received(1).ListarAsync(new FiltroOrdenes(null, AsignadasAMi: true), _usuario.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AsignadasConOtroValor_Responde400()
    {
        var respuesta = await Cliente(CodigosRol.IngenieroBiomedico).GetAsync($"{Ruta}?asignadas={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("asignadas", out _));
    }

    [Theory]
    [InlineData(CodigosRol.IngenieroBiomedico, HttpStatusCode.OK)]
    [InlineData(CodigosRol.AdministradorSistema, HttpStatusCode.Forbidden)]
    [InlineData(CodigosRol.TecnicoMantenimiento, HttpStatusCode.Forbidden)]
    [InlineData(CodigosRol.EnteRegulador, HttpStatusCode.Forbidden)]
    public async Task Tecnicos_SoloParaQuienAsigna(string rol, HttpStatusCode esperado)
    {
        var cliente = Cliente(rol);
        _repositorio.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([new TecnicoConCarga(_tecnico, "Carlos Ruiz", 2)]);

        var respuesta = await cliente.GetAsync($"{Ruta}/tecnicos");

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.OK)
        {
            Assert.Equal([new TecnicoConCarga(_tecnico, "Carlos Ruiz", 2)], (await respuesta.Content.ReadFromJsonAsync<TecnicoConCarga[]>(Json))!);
        }
    }

    [Fact]
    public async Task ObtenerPorId_ElTecnicoSoloLaSuya()
    {
        await Cliente(CodigosRol.IngenieroBiomedico).PostAsJsonAsync(Ruta, Cuerpo());
        var cliente = Cliente(CodigosRol.TecnicoMantenimiento);

        var respuesta = await cliente.GetAsync($"{Ruta}/{_guardada!.Id}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await _repositorio.Received().ObtenerAsync(_guardada.Id, _usuario.Id, Arg.Any<CancellationToken>());
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/{Guid.NewGuid()}")).StatusCode);
    }
}
