using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-018 Campos dinámicos (HU-006): consulta por tipo, borrador, publicación y permisos por rol.</summary>
public class CamposDinamicosViewModelTests
{
    private readonly IServicioConfiguracionCampos _servicio = Substitute.For<IServicioConfiguracionCampos>();
    private readonly IServicioCatalogos _catalogos = Substitute.For<IServicioCatalogos>();

    private static readonly ElementoCatalogo Monitor = new(Guid.NewGuid(), "Monitor de signos vitales", true);
    private static readonly ElementoCatalogo Bascula = new(Guid.NewGuid(), "Báscula", true);
    private static readonly ElementoCatalogo Antiguo = new(Guid.NewGuid(), "Tensiómetro de mercurio", false);

    private static readonly CampoDinamico Presion = new("presion_sistolica", "Presión sistólica", TiposDatoCampo.Numero, true, Unidad: "mmHg");

    private static ConfiguracionCampos Config(ElementoCatalogo tipo, int? version, params CampoDinamico[] campos) =>
        new(tipo.Id, tipo.Nombre, version, version is null ? null : DateTimeOffset.UtcNow, campos, []);

    private static ErrorApi Error(string? codigo, string mensaje, bool sinConexion = false) =>
        new(codigo, mensaje, new Dictionary<string, string>(), sinConexion);

    private async Task<CamposDinamicosViewModel> AbrirAsync(string rol)
    {
        _catalogos.ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>()).Returns([Monitor, Bascula, Antiguo]);
        _servicio.ObtenerAsync(Monitor.Id, Arg.Any<CancellationToken>()).Returns(Config(Monitor, 1, Presion));
        _servicio.ObtenerAsync(Bascula.Id, Arg.Any<CancellationToken>()).Returns(Config(Bascula, null));
        var vm = new CamposDinamicosViewModel(_servicio, _catalogos, EquiposPrueba.Sesion(rol));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private static void Agregar(CamposDinamicosViewModel vm, string etiqueta, string tipoDato, string opciones = "", bool obligatorio = false)
    {
        vm.AbrirAgregarCommand.Execute(null);
        vm.NuevaEtiqueta = etiqueta;
        vm.NuevoTipoDato = CamposDinamicosViewModel.OpcionesTipoDato.Single(o => o.Valor == tipoDato);
        vm.NuevasOpciones = opciones;
        vm.NuevoObligatorio = obligatorio;
        vm.AgregarCampoCommand.Execute(null);
    }

    // ---------- Consulta por tipo de equipo ----------

    [Fact]
    public async Task Cargar_ListaTiposActivosYMuestraLaVersionDelPrimero()
    {
        var vm = await AbrirAsync("AC-02");

        Assert.True(vm.Cargado);
        Assert.Equal([Monitor, Bascula], vm.TiposEquipo);
        Assert.Same(Monitor, vm.TipoSeleccionado);
        Assert.Equal("Versión publicada v1", vm.TextoVersion);
        Assert.Equal("Publicar v2", vm.TextoPublicar);
        var fila = Assert.Single(vm.Campos);
        Assert.Equal(("Presión sistólica (mmHg)", "Número entero", "Sí", "0"), (fila.EtiquetaConUnidad, fila.TipoDato, fila.Obligatorio, fila.Marcador));
        Assert.False(vm.HayCambios);
    }

    [Fact]
    public async Task TipoSinCampos_MuestraElMensajeVacio()
    {
        var vm = await AbrirAsync("AC-02");

        vm.TipoSeleccionado = Bascula;
        await Task.Yield();

        Assert.True(vm.SinCampos);
        Assert.Equal("Sin campos publicados", vm.TextoVersion);
        Assert.Equal("Publicar v1", vm.TextoPublicar);
        Assert.StartsWith("Báscula aún no tiene campos técnicos. Agrega", vm.TextoVacio);
    }

    [Fact]
    public async Task Cargar_SinConexion_AvisaYPermiteReintentar()
    {
        _catalogos.ListarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(Error(null, "Sin conexión con el servidor.", sinConexion: true));
        var vm = new CamposDinamicosViewModel(_servicio, _catalogos, EquiposPrueba.Sesion("AC-02"));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(vm.Cargado);
        Assert.Equal(("MSG-011", true), (vm.Aviso!.Codigo, vm.Aviso.PuedeReintentar));
    }

    // ---------- Borrador (AC-02) ----------

    [Fact]
    public async Task AgregarCampo_LoSumaAlBorradorSinPublicar()
    {
        var vm = await AbrirAsync("AC-02");

        Agregar(vm, "Estado de la batería", TiposDatoCampo.Seleccion, "Buena, Regular, Mala, buena", obligatorio: true);

        Assert.False(vm.Agregando);
        Assert.True(vm.HayCambios);
        Assert.Equal(["presion_sistolica", "estado_de_la_bateria"], vm.Campos.Select(c => c.Nombre));
        Assert.Equal("Buena · Regular · Mala", vm.Campos[1].Opciones);
        Assert.True(vm.Campos[1].EsSeleccion);
        Assert.True(vm.PublicarCommand.CanExecute(null));
        await _servicio.DidNotReceiveWithAnyArgs().PublicarAsync(default, default!, default);
    }

    [Fact]
    public async Task NombreInterno_SeDerivaDeLaEtiqueta()
    {
        var vm = await AbrirAsync("AC-02");

        vm.AbrirAgregarCommand.Execute(null);
        Assert.Equal("Lo que verá el técnico.", vm.NombreInterno);
        vm.NuevaEtiqueta = "Corriente de fuga (µA)";

        Assert.Equal("Nombre interno: corriente_de_fuga_a", vm.NombreInterno);
        Assert.Equal("campo_2_canal", CamposDinamicosViewModel.NombreDesde("2 canal"));
    }

    [Theory]
    [InlineData("ab", TiposDatoCampo.Texto, "", "Escribe la etiqueta visible.", null)]
    [InlineData("Presión sistólica", TiposDatoCampo.Texto, "", "Ya hay un campo con ese nombre.", null)]
    [InlineData("Estado", TiposDatoCampo.Seleccion, "Bueno", null, "Escribe al menos dos opciones separadas por coma.")]
    public async Task AgregarCampo_Invalido_MuestraElErrorYNoLoAgrega(string etiqueta, string tipo, string opciones, string? errorEtiqueta, string? errorOpciones)
    {
        var vm = await AbrirAsync("AC-02");

        Agregar(vm, etiqueta, tipo, opciones);

        Assert.Equal((errorEtiqueta, errorOpciones), (vm.ErrorEtiqueta, vm.ErrorOpciones));
        Assert.True(vm.Agregando);
        Assert.Single(vm.Campos);
        Assert.False(vm.HayCambios);
    }

    [Fact]
    public async Task UnidadSoloSeGuardaEnCamposNumericos()
    {
        var vm = await AbrirAsync("AC-02");

        vm.AbrirAgregarCommand.Execute(null);
        vm.NuevaEtiqueta = "Observación";
        vm.NuevaUnidad = "mmHg";
        vm.NuevoTipoDato = CamposDinamicosViewModel.OpcionesTipoDato.Single(o => o.Valor == TiposDatoCampo.Texto);
        Assert.False(vm.NuevoEsNumerico);
        vm.AgregarCampoCommand.Execute(null);

        Assert.Null(vm.Campos[1].Campo.Unidad);
        Assert.Equal("Texto", vm.Campos[1].Marcador);
    }

    [Fact]
    public async Task QuitarYDescartar_VuelvenALaVersionPublicada()
    {
        var vm = await AbrirAsync("AC-02");

        vm.QuitarCommand.Execute(vm.Campos[0]);
        Assert.True(vm.SinCampos);
        Assert.True(vm.HayCambios);
        Assert.False(vm.PublicarCommand.CanExecute(null));

        vm.DescartarCommand.Execute(null);

        Assert.False(vm.HayCambios);
        Assert.Equal(["presion_sistolica"], vm.Campos.Select(c => c.Nombre));
    }

    [Fact]
    public async Task CambiarDeTipo_ConservaElBorradorDeCadaTipo()
    {
        var vm = await AbrirAsync("AC-02");
        Agregar(vm, "Serie del sensor", TiposDatoCampo.Texto);

        vm.TipoSeleccionado = Bascula;
        await Task.Yield();
        Assert.False(vm.HayCambios);
        vm.TipoSeleccionado = Monitor;
        await Task.Yield();

        Assert.True(vm.HayCambios);
        Assert.Equal(2, vm.Campos.Count);
    }

    // ---------- Publicación ----------

    [Fact]
    public async Task Publicar_EnviaElBorradorYConfirmaLaVersionNueva()
    {
        var vm = await AbrirAsync("AC-02");
        Agregar(vm, "Serie del sensor", TiposDatoCampo.Texto);
        var serie = vm.Campos[1].Campo;
        _servicio.PublicarAsync(Monitor.Id, Arg.Any<IReadOnlyList<CampoDinamico>>(), Arg.Any<CancellationToken>())
            .Returns(Config(Monitor, 2, Presion, serie));

        await vm.PublicarCommand.ExecuteAsync(null);

        await _servicio.Received(1).PublicarAsync(
            Monitor.Id, Arg.Is<IReadOnlyList<CampoDinamico>>(c => c.Select(x => x.Nombre).SequenceEqual(new[] { "presion_sistolica", "serie_del_sensor" })), Arg.Any<CancellationToken>());
        Assert.Equal("Versión 2 publicada", vm.Confirmacion!.Titulo);
        Assert.Equal("Versión publicada v2", vm.TextoVersion);
        Assert.False(vm.HayCambios);
        Assert.False(vm.PublicarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Publicar_ConConflicto_ConservaElBorradorYMuestraElCodigo()
    {
        var vm = await AbrirAsync("AC-02");
        Agregar(vm, "Serie del sensor", TiposDatoCampo.Texto);
        _servicio.PublicarAsync(Monitor.Id, Arg.Any<IReadOnlyList<CampoDinamico>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error("CFG-001", "Otra persona publicó una versión al mismo tiempo."));

        await vm.PublicarCommand.ExecuteAsync(null);

        Assert.Equal(("CFG-001", "No se pudo publicar la versión"), (vm.Aviso!.Codigo, vm.Aviso.Titulo));
        Assert.True(vm.HayCambios);
        Assert.Equal(2, vm.Campos.Count);
        Assert.Null(vm.Confirmacion);
    }

    // ---------- CA-HU-006-03: el Administrador solo consulta ----------

    [Fact]
    public async Task Administrador_SoloConsulta()
    {
        var vm = await AbrirAsync("AC-01");

        Assert.False(vm.PuedeGestionar);
        vm.AbrirAgregarCommand.Execute(null);
        Assert.False(vm.Agregando);
        vm.QuitarCommand.Execute(vm.Campos[0]);
        Assert.False(vm.HayCambios);
        Assert.False(vm.PublicarCommand.CanExecute(null));

        vm.TipoSeleccionado = Bascula;
        await Task.Yield();
        Assert.EndsWith("El Ingeniero Biomédico puede definirlos.", vm.TextoVacio);
    }
}
