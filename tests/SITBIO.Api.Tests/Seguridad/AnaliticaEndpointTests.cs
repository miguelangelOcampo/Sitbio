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
using Sitbio.Application.Analitica;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Seguridad;

/// <summary>
/// Contrato HTTP del recálculo bajo demanda (I-04, HU-008): solo AC-02 lo pide (202 y el lote corre en segundo
/// plano); los roles con analítica consultan su estado. Sin base de datos ni Python: el ejecutor es un sustituto.
/// </summary>
public class AnaliticaEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";
    private static readonly string Ruta = $"{RutasApi.V1}/analitica/agrupamiento";
    private readonly IRepositorioAnalitica _repositorio = Substitute.For<IRepositorioAnalitica>();
    private readonly IEjecutorAnalitica _ejecutor = Substitute.For<IEjecutorAnalitica>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly IRepositorioSesiones _sesiones = Substitute.For<IRepositorioSesiones>();
    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly Usuario _usuario = new(Guid.NewGuid(), 1, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly WebApplicationFactory<Program> _fabrica;

    public AnaliticaEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs((ResultadoAnalitica?)null);
        _sesiones.ConsultarEstadoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EstadoSesion.Activa);
    }

    private WebApplicationFactory<Program> Api() => _fabrica.WithWebHostBuilder(b => b
        .UseSetting("Jwt:Clave", ClaveJwt)
        .ConfigureTestServices(s => s
            .AddScoped(_ => _repositorio)
            .AddScoped(_ => _ejecutor)
            .AddScoped(_ => _instituciones)
            .AddScoped(_ => _sesiones)
            .AddScoped(_ => _bitacora)
            .AddScoped(_ => _auditoria)));

    private HttpClient Cliente(WebApplicationFactory<Program> api, string? codigoRol)
    {
        var cliente = api.CreateClient();
        if (codigoRol is not null)
        {
            var opciones = new OpcionesJwt { Issuer = "sitbio-api", Audience = "sitbio-desktop", Clave = ClaveJwt, MinutosVigencia = 30 };
            var token = new GeneradorJwt(Options.Create(opciones), TimeProvider.System).Generar(_usuario, codigoRol).Token;
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    private static async Task<string> EsperarEstadoAsync(HttpClient cliente, string distintoDe)
    {
        for (var intento = 0; intento < 100; intento++)
        {
            var estado = (await cliente.GetFromJsonAsync<JsonElement>(Ruta)).GetProperty("estado").GetString()!;
            if (estado != distintoDe)
            {
                return estado;
            }

            await Task.Delay(50);
        }

        return distintoDe;
    }

    [Theory]
    [InlineData(CodigosRol.GerenteBiomedico)]
    [InlineData(CodigosRol.DirectivaInstitucional)]
    [InlineData(CodigosRol.EnteRegulador)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    [InlineData(CodigosRol.AdministradorSistema)]
    public async Task Recalcular_SoloElIngeniero_LosDemasReciben403(string rol)
    {
        using var api = Api();

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(api, rol).PostAsync(Ruta, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(api, null).PostAsync(Ruta, null)).StatusCode);
        await _ejecutor.DidNotReceiveWithAnyArgs().EjecutarAsync(default, default);
    }

    [Fact]
    public async Task Estado_SinSolicitud_LoConsultanLosRolesConAnalitica()
    {
        using var api = Api();

        var cuerpo = await Cliente(api, CodigosRol.DirectivaInstitucional).GetFromJsonAsync<JsonElement>(Ruta);

        Assert.Equal(EstadosRecalculo.SinSolicitud, cuerpo.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("ultimoCalculo").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(api, CodigosRol.PersonalClinico).GetAsync(Ruta)).StatusCode);
    }

    [Fact]
    public async Task Recalcular_Responde202_NoAdmiteOtroEnCurso_YTermina()
    {
        var liberar = new TaskCompletionSource();
        _ejecutor.EjecutarAsync(default, default).ReturnsForAnyArgs(_ => liberar.Task);
        using var api = Api();
        var ingeniero = Cliente(api, CodigosRol.IngenieroBiomedico);

        var primera = await ingeniero.PostAsync(Ruta, null);
        Assert.Equal(HttpStatusCode.Accepted, primera.StatusCode);
        Assert.Equal(EstadosRecalculo.EnCurso, (await primera.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString());

        var segunda = await ingeniero.PostAsync(Ruta, null);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal(RecalculoAnalitica.CodigoEnCurso, (await segunda.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());

        liberar.SetResult();
        Assert.Equal(EstadosRecalculo.Terminado, await EsperarEstadoAsync(ingeniero, EstadosRecalculo.EnCurso));
        await _auditoria.ReceivedWithAnyArgs(1).RegistrarAsync(default, AccionesAuditoria.Actualizar, RecalculoAnalitica.Entidad, default, default, default);

        // Terminado el anterior, se puede volver a pedir.
        Assert.Equal(HttpStatusCode.Accepted, (await ingeniero.PostAsync(Ruta, null)).StatusCode);
    }

    [Fact]
    public async Task Recalcular_SiElLoteFalla_ElEstadoLoDice()
    {
        _ejecutor.EjecutarAsync(default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("código 1"));
        using var api = Api();
        var ingeniero = Cliente(api, CodigosRol.IngenieroBiomedico);

        Assert.Equal(HttpStatusCode.Accepted, (await ingeniero.PostAsync(Ruta, null)).StatusCode);

        Assert.Equal(EstadosRecalculo.Fallido, await EsperarEstadoAsync(ingeniero, EstadosRecalculo.EnCurso));
        var cuerpo = await ingeniero.GetFromJsonAsync<JsonElement>(Ruta);
        Assert.Equal(CoordinadorRecalculos.MensajeFallo, cuerpo.GetProperty("mensaje").GetString());
    }
}
