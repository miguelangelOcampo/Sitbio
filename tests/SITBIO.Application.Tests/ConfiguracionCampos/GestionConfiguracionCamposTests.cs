using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.ConfiguracionCampos;

/// <summary>Motor de campos dinámicos (HU-006): consulta, publicación de versiones, permisos y bitácora.</summary>
public class GestionConfiguracionCamposTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioConfiguracionCampos _repositorio = Substitute.For<IRepositorioConfiguracionCampos>();
    private readonly IRepositorioCatalogos _catalogos = Substitute.For<IRepositorioCatalogos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly TipoEquipo _bomba = new(Tenant, "Bomba de infusión");
    private readonly GestionConfiguracionCampos _caso;

    private static readonly CampoDinamico Goteo = new("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h");

    public GestionConfiguracionCamposTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_usuario);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _catalogos.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IElementoCatalogo?)null);
        _catalogos.ObtenerAsync(TiposCatalogo.TiposEquipo, _bomba.Id, Arg.Any<CancellationToken>()).Returns(_bomba);
        _repositorio.VersionesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        var tiempo = Substitute.For<TimeProvider>();
        tiempo.GetUtcNow().Returns(Ahora);
        _caso = new GestionConfiguracionCampos(_repositorio, _catalogos, _auditoria, _contexto, tiempo);
    }

    private VersionConfiguracion Version(int numero, string estado, params CampoDinamico[] campos) =>
        new(_bomba.Id, numero, estado, Guid.NewGuid(), Ahora.AddDays(-numero), campos);

    private void ConVersiones(params VersionConfiguracion[] versiones) =>
        _repositorio.VersionesAsync(_bomba.Id, Arg.Any<CancellationToken>()).Returns(versiones);

    // ---------- Consulta ----------

    [Fact]
    public async Task Obtener_SinVersiones_DevuelveElTipoSinCampos()
    {
        var configuracion = await _caso.ObtenerAsync(_bomba.Id, CancellationToken.None);

        Assert.Equal(("Bomba de infusión", (int?)null), (configuracion.TipoEquipo, configuracion.Version));
        Assert.Empty(configuracion.Campos);
        Assert.Empty(configuracion.Versiones);
    }

    [Fact]
    public async Task Obtener_DevuelveLaVersionActivaYElHistorial()
    {
        var volumen = new CampoDinamico("volumen_total", "Volumen total", TiposDatoCampo.Numero, false, null, "ml");
        ConVersiones(Version(2, EstadosConfiguracion.Activa, Goteo, volumen), Version(1, EstadosConfiguracion.Historica, Goteo));

        var configuracion = await _caso.ObtenerAsync(_bomba.Id, CancellationToken.None);

        Assert.Equal(2, configuracion.Version);
        Assert.Equal([Goteo, volumen], configuracion.Campos);
        Assert.Equal([(2, 2), (1, 1)], configuracion.Versiones.Select(v => (v.Version, v.Campos)));
    }

    [Fact]
    public async Task Obtener_TipoDeOtraInstitucion_Responde404()
    {
        // CA-HU-006-02: el repositorio de catálogos filtra por tenant; un tipo ajeno no aparece.
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));
        await _repositorio.DidNotReceiveWithAnyArgs().VersionesAsync(default, default);
    }

    // ---------- Publicación (AC-02) ----------

    [Fact]
    public async Task Publicar_PrimeraVersion_GuardaLaVersion1ActivaYLaRegistraEnLaBitacora()
    {
        var publicada = await _caso.PublicarAsync(_bomba.Id, new DatosConfiguracionCampos([new CampoDinamico(null, "Tasa de goteo", "numero_decimal", true, Unidad: "ml/h")]), CancellationToken.None);

        await _repositorio.Received(1).PublicarAsync(
            Arg.Is<VersionConfiguracion>(v => v.Version == 1 && v.Estado == EstadosConfiguracion.Activa && v.CreadoPor == _usuario
                && v.FechaCreacion == Ahora && v.Campos.Single().Nombre == "tasa_de_goteo"),
            Arg.Any<CancellationToken>());
        Assert.Equal((1, Ahora), (publicada.Version, publicada.FechaPublicacion));
        await _auditoria.Received(1).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Crear,
            GestionConfiguracionCampos.Entidad,
            _bomba.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>?>(d => d!["version"] == "1" && d["tipo_equipo"] == "Bomba de infusión" && d["campos"] == "tasa_de_goteo"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publicar_SobreVersionesAnteriores_UsaLaSiguienteYLasDejaHistoricas()
    {
        ConVersiones(Version(2, EstadosConfiguracion.Activa, Goteo), Version(1, EstadosConfiguracion.Historica, Goteo with { Obligatorio = false }));
        var alarma = new CampoDinamico("alarma_activa", "Alarma activa", TiposDatoCampo.Booleano);

        var publicada = await _caso.PublicarAsync(_bomba.Id, new DatosConfiguracionCampos([Goteo, alarma]), CancellationToken.None);

        await _repositorio.Received(1).PublicarAsync(Arg.Is<VersionConfiguracion>(v => v.Version == 3), Arg.Any<CancellationToken>());
        Assert.Equal(3, publicada.Version);
        Assert.Equal(
            [(3, EstadosConfiguracion.Activa), (2, EstadosConfiguracion.Historica), (1, EstadosConfiguracion.Historica)],
            publicada.Versiones.Select(v => (v.Version, v.Estado)));
    }

    [Fact]
    public async Task Publicar_SinCambiosFrenteALaActiva_LoRechaza()
    {
        ConVersiones(Version(1, EstadosConfiguracion.Activa, Goteo));

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.PublicarAsync(_bomba.Id, new DatosConfiguracionCampos([Goteo with { Etiqueta = " Tasa de goteo " }]), CancellationToken.None));

        Assert.Equal(["La versión 1 ya tiene estos campos; cambia algo antes de publicar."], error.Errores["campos"]);
        await _repositorio.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
    }

    [Fact]
    public async Task Publicar_CamposInvalidos_Responde400PorCampo()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.PublicarAsync(_bomba.Id, new DatosConfiguracionCampos([new CampoDinamico(null, "Resultado", TiposDatoCampo.Seleccion, Opciones: ["Pasa"])]), CancellationToken.None));

        Assert.True(error.Errores.ContainsKey("campos[0].opciones"));
        await _repositorio.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!);
    }

    [Theory]
    [InlineData(CodigosRol.AdministradorSistema)]
    [InlineData(CodigosRol.TecnicoMantenimiento)]
    public async Task CA_HU_006_03_SoloElIngenieroPublica(string rol)
    {
        _contexto.Rol.Returns(rol);

        var error = await Assert.ThrowsAsync<ErrorPermiso>(() =>
            _caso.PublicarAsync(_bomba.Id, new DatosConfiguracionCampos([Goteo]), CancellationToken.None));

        Assert.Equal("Solo el Ingeniero Biomédico define y publica los campos dinámicos; puedes consultarlos.", error.Message);
        await _repositorio.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
    }

    [Fact]
    public async Task Publicar_TipoInexistente_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            _caso.PublicarAsync(Guid.NewGuid(), new DatosConfiguracionCampos([Goteo]), CancellationToken.None));
    }
}
