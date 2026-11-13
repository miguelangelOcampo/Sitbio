using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP de la ejecución de órdenes (API-OT-ESTADO, API-INT y API-OT-APROB; HU-005, HU-021): solo el
/// Técnico (AC-03) inicia, pausa, registra la intervención y cierra; solo el Ingeniero (AC-02) aprueba y firma;
/// consultan la intervención AC-01, AC-02, AC-03 y AC-07. Sin base de datos.
/// </summary>
public class EjecucionOrdenesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private const string ObjectId = "65f1a2b3c4d5e6f7a8b9c0d1";
    private static readonly string Ruta = $"{RutasApi.V1}/ordenes";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IRepositorioOrdenes _ordenes = Substitute.For<IRepositorioOrdenes>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioConfiguracionCampos _configuracion = Substitute.For<IRepositorioConfiguracionCampos>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Carlos", "Ruiz", "carlos@demo.co", "hash");
    private readonly Guid _tipo = Guid.NewGuid();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly Tiquete _tiquete;
    private readonly OrdenTrabajo _orden;

    public EjecucionOrdenesEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _tiquete = new Tiquete(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, Criticidades.Alta, DateTimeOffset.UtcNow);
        _tiquete.Numerar(1);
        (_orden, _) = OrdenTrabajo.Correctiva(_tiquete, _usuario.Id, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow);
    }

    private static readonly CampoDinamico Goteo = new("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h");

    private HttpClient Cliente(string? codigoRol)
    {
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
        var cliente = _fabrica.WithWebHostBuilder(b => b
                .UseSetting("Jwt:Clave", ClaveJwt)
                .ConfigureTestServices(s => s
                    .AddScoped(_ => _ordenes)
                    .AddScoped(_ => _intervenciones)
                    .AddScoped(_ => _configuracion)
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

    private static object Intervencion(double goteo = 20.5) => new
    {
        diagnostico = "Sensor descalibrado",
        accionesRealizadas = "Calibración",
        repuestos = new[] { new { nombre = "Sensor", cantidad = 1, costoUnitario = 15000 } },
        estadoEquipoSalida = "Operativo",
        camposDinamicos = new Dictionary<string, object> { ["tasa_goteo"] = goteo },
    };

    private void Preparar()
    {
        _ordenes.OrdenAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(_orden);
        _ordenes.ObtenerAsync(_orden.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new OrdenLeida(_orden, "PL-001", "SN-1", "Bomba", "UCI", "Carlos Ruiz", _tiquete.Codigo, _tiquete.TipoFalla));
        _ordenes.TiqueteAsync(_tiquete.Id, Arg.Any<CancellationToken>()).Returns(_tiquete);
        _ordenes.TipoEquipoAsync(_orden.EquipoId, Arg.Any<CancellationToken>()).Returns(_tipo);
        _configuracion.VersionesAsync(_tipo, Arg.Any<CancellationToken>())
            .Returns([new VersionConfiguracion(_tipo, 1, EstadosConfiguracion.Activa, Guid.NewGuid(), DateTimeOffset.UtcNow, [Goteo])]);
        _intervenciones.ObtenerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IntervencionGuardada?)null);
        _intervenciones.GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ObjectId);
    }

    [Fact]
    public async Task SinToken_Responde401()
    {
        var cliente = Cliente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "iniciar" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", Intervencion())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync($"{Ruta}/{_orden.Id}/intervencion")).StatusCode);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.IngenieroBiomedico)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.EnteRegulador)]
    public async Task SoloElTecnico_IniciaPausaYRegistra(string rol)
    {
        Preparar();
        var cliente = Cliente(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "iniciar" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", Intervencion())).StatusCode);
        Assert.Equal(EstadosOrden.Asignada, _orden.Estado);
    }

    [Fact]
    public async Task Tecnico_IniciaYRegistraLaIntervencion()
    {
        Preparar();
        var cliente = Cliente(CodigosRol.TecnicoMantenimiento);

        var iniciada = await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "iniciar" });
        Assert.Equal(HttpStatusCode.OK, iniciada.StatusCode);
        Assert.Equal(EstadosOrden.EnEjecucion, (await iniciada.Content.ReadFromJsonAsync<OrdenDto>(Json))!.Estado);

        var registrada = await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", Intervencion());
        Assert.Equal(HttpStatusCode.OK, registrada.StatusCode);
        using var cuerpo = JsonDocument.Parse(await registrada.Content.ReadAsStringAsync());
        Assert.Equal(20.5, cuerpo.RootElement.GetProperty("camposDinamicos").GetProperty("tasa_goteo").GetDouble());
        Assert.Equal(15000m, cuerpo.RootElement.GetProperty("costoTotal").GetDecimal());
        Assert.Equal(ObjectId, _orden.IntervencionId);
    }

    [Fact]
    public async Task ErroresDeLaIntervencion_SeRespondenConSuCodigo()
    {
        Preparar();
        var cliente = Cliente(CodigosRol.TecnicoMantenimiento);

        // Sin iniciar: 409 OT-002.
        var sinIniciar = await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", Intervencion());
        Assert.Equal(HttpStatusCode.Conflict, sinIniciar.StatusCode);
        using (var problema = JsonDocument.Parse(await sinIniciar.Content.ReadAsStringAsync()))
        {
            Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, problema.RootElement.GetProperty("codigo").GetString());
        }

        await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "iniciar" });

        // Campo dinámico de otro tipo: 400 con la clave del campo.
        var invalida = await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", new { diagnostico = "D", accionesRealizadas = "A", camposDinamicos = new { tasa_goteo = "alta" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        using (var problema = JsonDocument.Parse(await invalida.Content.ReadAsStringAsync()))
        {
            Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("camposDinamicos.tasa_goteo", out _));
        }

        // MongoDB caído: 503 y la orden sin enlace (RT-002).
        _intervenciones.GuardarAsync(Arg.Any<Intervencion>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorServicioNoDisponible(new TimeoutException()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await cliente.PutAsJsonAsync($"{Ruta}/{_orden.Id}/intervencion", Intervencion())).StatusCode);
        Assert.Null(_orden.IntervencionId);

        // Cerrar sin intervención: 409 OT-002; acción desconocida: 400; orden ajena: 404.
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "cerrar" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "aprobar" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.PatchAsJsonAsync($"{Ruta}/{Guid.NewGuid()}/estado", new { accion = "iniciar" })).StatusCode);
    }

    // ---------- HU-021: cierre y aprobación ----------

    private void ConIntervencion()
    {
        Preparar();
        var guardada = new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _usuario.Id, TiposMantenimiento.Correctivo, "D", "A", [], null, 0m, null, "Operativo",
            new Dictionary<string, object>(), 1, DateTimeOffset.UtcNow, false);
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectId, guardada));
        var equipo = new EquipoBiomedico(Guid.NewGuid(), _tipo, Guid.NewGuid(), "SN-1", "PL-001", "Baxter", "Sigma", DateTimeOffset.UtcNow);
        _ordenes.EquipoAsync(_orden.EquipoId, Arg.Any<CancellationToken>()).Returns(new EquipoDeLaOrden(equipo, "Bomba", "UCI", null));
    }

    [Fact]
    public async Task Tecnico_CierraSuOrden_YElIngenieroLaApruebaConSuFirma()
    {
        ConIntervencion();
        _orden.Iniciar(DateTimeOffset.UtcNow);
        _orden.EnlazarIntervencion(ObjectId);

        var cerrada = await Cliente(CodigosRol.TecnicoMantenimiento).PatchAsJsonAsync($"{Ruta}/{_orden.Id}/estado", new { accion = "cerrar" });
        Assert.Equal(HttpStatusCode.OK, cerrada.StatusCode);
        Assert.Equal(EstadosOrden.Cerrada, (await cerrada.Content.ReadFromJsonAsync<OrdenDto>(Json))!.Estado);

        var ingeniero = Cliente(CodigosRol.IngenieroBiomedico);
        Assert.Equal(HttpStatusCode.BadRequest, (await ingeniero.PostAsJsonAsync($"{Ruta}/{_orden.Id}/aprobacion", new { firma = " " })).StatusCode);

        var aprobada = await ingeniero.PostAsJsonAsync($"{Ruta}/{_orden.Id}/aprobacion", new { firma = "Laura Gómez" });
        Assert.Equal(HttpStatusCode.OK, aprobada.StatusCode);
        var dto = (await aprobada.Content.ReadFromJsonAsync<OrdenDto>(Json))!;
        Assert.Equal((EstadosOrden.Aprobada, "Laura Gómez", (Guid?)_usuario.Id), (dto.Estado, dto.FirmaAprobacion, dto.AprobadoPorId));
        await _intervenciones.Received(1).FirmarAsync(ObjectId, "Operativo", Arg.Any<FotoEquipo>(), Arg.Any<CancellationToken>());

        // RN-004: otra aprobación responde 409 con su código.
        var otraVez = await ingeniero.PostAsJsonAsync($"{Ruta}/{_orden.Id}/aprobacion", new { firma = "Otra" });
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        using var problema = JsonDocument.Parse(await otraVez.Content.ReadAsStringAsync());
        Assert.Equal(GestionEjecucion.CodigoFirmada, problema.RootElement.GetProperty("codigo").GetString());
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.PersonalClinico)]
    [InlineData(CodigosRol.EnteRegulador)]
    [InlineData(null)]
    public async Task SoloElIngeniero_ApruebaYFirma(string? rol)
    {
        ConIntervencion();
        _orden.Iniciar(DateTimeOffset.UtcNow);
        _orden.EnlazarIntervencion(ObjectId);
        _orden.Cerrar(DateTimeOffset.UtcNow);

        var respuesta = await Cliente(rol).PostAsJsonAsync($"{Ruta}/{_orden.Id}/aprobacion", new { firma = "Laura Gómez" });

        Assert.Equal(rol is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(EstadosOrden.Cerrada, _orden.Estado);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema, HttpStatusCode.OK)]
    [InlineData(CodigosRol.IngenieroBiomedico, HttpStatusCode.OK)]
    [InlineData(CodigosRol.TecnicoMantenimiento, HttpStatusCode.OK)]
    [InlineData(CodigosRol.EnteRegulador, HttpStatusCode.OK)]
    [InlineData(CodigosRol.PersonalClinico, HttpStatusCode.Forbidden)]
    public async Task ConsultarIntervencion_PorRol(string rol, HttpStatusCode esperado)
    {
        Preparar();
        var guardada = new Intervencion(
            Guid.NewGuid(), _orden.Id, _orden.EquipoId, _usuario.Id, TiposMantenimiento.Correctivo, "D", "A", [], null, 0m, null, null,
            new Dictionary<string, object> { ["tasa_goteo"] = 20.5 }, 1, DateTimeOffset.UtcNow, false);
        _intervenciones.ObtenerAsync(_orden.Id, Arg.Any<CancellationToken>()).Returns(new IntervencionGuardada(ObjectId, guardada));

        var respuesta = await Cliente(rol).GetAsync($"{Ruta}/{_orden.Id}/intervencion");

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.OK)
        {
            Assert.Equal(guardada.Id, (await respuesta.Content.ReadFromJsonAsync<IntervencionDto>(Json))!.Id);
        }
    }
}
