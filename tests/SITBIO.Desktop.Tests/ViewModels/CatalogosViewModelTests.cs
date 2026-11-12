using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-020 Catálogos (HU-025): consulta, alta, inactivación y valores por defecto de los tipos de equipo.</summary>
public class CatalogosViewModelTests
{
    private readonly IServicioCatalogos _servicio = Substitute.For<IServicioCatalogos>();

    private static readonly ElementoCatalogo Monitor = new(Guid.NewGuid(), "Monitor de signos vitales", true, "IIb", true, 12, "Signos vitales");
    private static readonly ElementoCatalogo Bascula = new(Guid.NewGuid(), "Báscula", true, null, false, null);
    private static readonly ElementoCatalogo Antiguo = new(Guid.NewGuid(), "Tensiómetro de mercurio", false);
    private static readonly ElementoCatalogo Urgencias = new(Guid.NewGuid(), "Urgencias", true);

    private async Task<CatalogosViewModel> AbrirAsync(string rol)
    {
        _servicio.ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>()).Returns([Monitor, Bascula, Antiguo]);
        _servicio.ListarAsync(TiposCatalogo.Ubicaciones, Arg.Any<CancellationToken>()).Returns([Urgencias]);
        _servicio.ListarAsync(TiposCatalogo.Proveedores, Arg.Any<CancellationToken>()).Returns([]);
        _servicio.ListarAsync(TiposCatalogo.TiposFalla, Arg.Any<CancellationToken>()).Returns([]);
        var vm = new CatalogosViewModel(_servicio, EquiposPrueba.Sesion(rol));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private static CatalogoVista Catalogo(CatalogosViewModel vm, string tipo) => vm.Catalogos.Single(c => c.Tipo == tipo);

    private static ErrorApi Error(string? codigo, string mensaje, bool sinConexion = false) =>
        new(codigo, mensaje, new Dictionary<string, string>(), sinConexion);

    // ---------- CA-HU-025-01: los cuatro catálogos con sus valores ----------

    [Fact]
    public async Task Cargar_MuestraLosCuatroCatalogosConSusActivos()
    {
        var vm = await AbrirAsync("AC-02");

        Assert.Equal(["Tipos de equipo", "Ubicaciones", "Proveedores", "Tipos de falla"], vm.Catalogos.Select(c => c.Nombre));
        Assert.Equal(["2 activos", "1 activos", "0 activos", "0 activos"], vm.Catalogos.Select(c => c.TextoActivos));
        Assert.Equal(TiposCatalogo.TiposEquipo, vm.Seleccionado!.Tipo);
        Assert.True(vm.Seleccionado.Elegido);
        Assert.Equal("2 activos de 3", vm.Resumen);
    }

    [Fact]
    public async Task TiposDeEquipo_MuestranClaseYCalibracionPorDefecto()
    {
        var vm = await AbrirAsync("AC-02");

        Assert.True(vm.EsTiposEquipo);
        Assert.Equal(["Clase IIb", "Por definir", "Por definir"], vm.Filas.Select(f => f.ClaseRiesgo));
        Assert.Equal(["Sí · cada 12 meses", "No requiere", "Por definir"], vm.Filas.Select(f => f.Calibracion));
        Assert.Equal(["Activo", "Activo", "Inactivo"], vm.Filas.Select(f => f.Estado));
        Assert.Equal(["Inactivar", "Inactivar", "Activar"], vm.Filas.Select(f => f.Accion));
    }

    [Fact]
    public async Task Cargar_SinConexion_AvisaYPermiteReintentar()
    {
        _servicio.ListarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(Error(null, "Sin conexión con el servidor.", sinConexion: true));
        var vm = new CatalogosViewModel(_servicio, EquiposPrueba.Sesion("AC-02"));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("MSG-011", true), (vm.Aviso!.Codigo, vm.Aviso.PuedeReintentar));
    }

    // ---------- CA-HU-025-03: AC-01 gestiona solo ubicaciones ----------

    [Theory]
    [InlineData("AC-02", TiposCatalogo.TiposEquipo, true)]
    [InlineData("AC-02", TiposCatalogo.TiposFalla, true)]
    [InlineData("AC-01", TiposCatalogo.Ubicaciones, true)]
    [InlineData("AC-01", TiposCatalogo.TiposEquipo, false)]
    [InlineData("AC-01", TiposCatalogo.Proveedores, false)]
    public async Task PuedeGestionar_SegunRolYCatalogo(string rol, string tipo, bool puede)
    {
        var vm = await AbrirAsync(rol);

        vm.ElegirCommand.Execute(Catalogo(vm, tipo));

        Assert.Equal(puede, vm.PuedeGestionar);
        Assert.Equal(puede, vm.AgregarCommand.CanExecute(null));
        Assert.Equal(!puede, vm.Resumen.EndsWith("solo consulta (lo gestiona el Ingeniero Biomédico)", StringComparison.Ordinal));
    }

    // ---------- Alta de valores ----------

    [Fact]
    public async Task Agregar_ValorVacio_PideElValor()
    {
        var vm = await AbrirAsync("AC-01");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.Ubicaciones));

        await vm.AgregarCommand.ExecuteAsync(null);

        Assert.Equal("Escribe el valor.", vm.ErrorNuevo);
        await _servicio.DidNotReceiveWithAnyArgs().CrearAsync(default!, default!, default);
    }

    [Fact]
    public async Task Agregar_ValorRepetido_LoRechazaSinLlamarALaApi()
    {
        var vm = await AbrirAsync("AC-01");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.Ubicaciones));
        vm.NuevoValor = "  urgencias ";

        await vm.AgregarCommand.ExecuteAsync(null);

        Assert.Equal("Ese valor ya existe en la lista.", vm.ErrorNuevo);
        await _servicio.DidNotReceiveWithAnyArgs().CrearAsync(default!, default!, default);
    }

    [Fact]
    public async Task Agregar_CreaElValorYRecargaLaLista()
    {
        var vm = await AbrirAsync("AC-01");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.Ubicaciones));
        var uci = new ElementoCatalogo(Guid.NewGuid(), "UCI adultos", true);
        _servicio.CrearAsync(TiposCatalogo.Ubicaciones, Arg.Any<DatosCatalogo>(), Arg.Any<CancellationToken>()).Returns(uci);
        _servicio.ListarAsync(TiposCatalogo.Ubicaciones, Arg.Any<CancellationToken>()).Returns([Urgencias, uci]);
        vm.NuevoValor = " UCI adultos ";

        await vm.AgregarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CrearAsync(TiposCatalogo.Ubicaciones, new DatosCatalogo("UCI adultos"), Arg.Any<CancellationToken>());
        Assert.Equal(["Urgencias", "UCI adultos"], vm.Filas.Select(f => f.Valor));
        Assert.Equal("2 activos", Catalogo(vm, TiposCatalogo.Ubicaciones).TextoActivos);
        Assert.Equal(string.Empty, vm.NuevoValor);
        Assert.Equal("Valor agregado", vm.Confirmacion!.Titulo);
        Assert.Equal("“UCI adultos” ya está disponible en ubicaciones.", vm.Confirmacion.Mensaje);
    }

    [Fact]
    public async Task Agregar_Proveedor_ExigeNitYTipo()
    {
        var vm = await AbrirAsync("AC-02");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.Proveedores));
        vm.NuevoValor = "Biomédica Andina";

        await vm.AgregarCommand.ExecuteAsync(null);

        Assert.True(vm.EsProveedores);
        Assert.Equal("Escribe el NIT y el tipo de proveedor.", vm.ErrorNuevo);
    }

    [Fact]
    public async Task Agregar_Proveedor_EnviaNitYTipo()
    {
        var vm = await AbrirAsync("AC-02");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.Proveedores));
        _servicio.CrearAsync(Arg.Any<string>(), Arg.Any<DatosCatalogo>(), Arg.Any<CancellationToken>())
            .Returns(new ElementoCatalogo(Guid.NewGuid(), "Biomédica Andina", true));
        (vm.NuevoValor, vm.NuevoNit, vm.NuevoTipoProveedor) = ("Biomédica Andina", "900.111.222-3", "Servicio técnico");

        await vm.AgregarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CrearAsync(
            TiposCatalogo.Proveedores,
            new DatosCatalogo("Biomédica Andina", Nit: "900.111.222-3", TipoProveedor: "Servicio técnico"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agregar_DuplicadoEnLaApi_MuestraElMensajeDelConflicto()
    {
        var vm = await AbrirAsync("AC-02");
        vm.ElegirCommand.Execute(Catalogo(vm, TiposCatalogo.TiposFalla));
        _servicio.CrearAsync(Arg.Any<string>(), Arg.Any<DatosCatalogo>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error("CAT-001", "«Eléctrica» ya está en los tipos de falla de tu institución. Si está inactivo, actívalo."));
        vm.NuevoValor = "Eléctrica";

        await vm.AgregarCommand.ExecuteAsync(null);

        Assert.Equal("CAT-001", vm.Aviso!.Codigo);
        Assert.StartsWith("«Eléctrica» ya está", vm.ErrorNuevo);
    }

    // ---------- CA-HU-025-02: inactivar en lugar de borrar ----------

    [Fact]
    public async Task CambiarEstado_InactivaEnviandoElDetalleCompleto()
    {
        var vm = await AbrirAsync("AC-02");
        _servicio.ObtenerAsync(TiposCatalogo.TiposEquipo, Monitor.Id, Arg.Any<CancellationToken>()).Returns(Monitor);
        _servicio.ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>()).Returns([Monitor with { Activo = false }, Bascula, Antiguo]);

        await vm.CambiarEstadoCommand.ExecuteAsync(vm.Filas[0]);

        await _servicio.Received(1).ActualizarAsync(
            TiposCatalogo.TiposEquipo, Monitor.Id, Monitor.ComoDatos(activo: false), Arg.Any<CancellationToken>());
        Assert.Equal("Inactivo", vm.Filas[0].Estado);
        Assert.Equal("1 activos de 3", vm.Resumen);
        Assert.Equal("Valor inactivado", vm.Confirmacion!.Titulo);
    }

    [Fact]
    public async Task CambiarEstado_SinPermiso_NoLlamaALaApi()
    {
        var vm = await AbrirAsync("AC-01");

        await vm.CambiarEstadoCommand.ExecuteAsync(vm.Filas[0]);

        await _servicio.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default!, default);
    }

    // ---------- RF-030: valores por defecto de los tipos de equipo (AC-02) ----------

    [Fact]
    public async Task ElegirTipo_CargaSusValoresParaEditar()
    {
        var vm = await AbrirAsync("AC-02");
        Assert.True(vm.AyudaValores);

        vm.FilaSeleccionada = vm.Filas[0];

        Assert.True(vm.EditandoValores);
        Assert.False(vm.AyudaValores);
        Assert.Equal(("IIb", true, "12"), (vm.Clase.Valor, vm.RequiereCalibracion, vm.Meses));
    }

    [Fact]
    public async Task AC01_NoEditaLosValoresDeLosTipos()
    {
        var vm = await AbrirAsync("AC-01");

        vm.FilaSeleccionada = vm.Filas[0];

        Assert.False(vm.EditandoValores);
        Assert.False(vm.AyudaValores);
    }

    [Fact]
    public async Task GuardarValores_ConservaLaDescripcionYEnviaLaClaseYLaCalibracion()
    {
        var vm = await AbrirAsync("AC-02");
        _servicio.ObtenerAsync(TiposCatalogo.TiposEquipo, Monitor.Id, Arg.Any<CancellationToken>()).Returns(Monitor);
        vm.FilaSeleccionada = vm.Filas[0];
        vm.Clase = CatalogosViewModel.OpcionesClase.Single(o => o.Valor == "III");
        vm.Meses = "6";

        await vm.GuardarValoresCommand.ExecuteAsync(null);

        await _servicio.Received(1).ActualizarAsync(
            TiposCatalogo.TiposEquipo,
            Monitor.Id,
            Arg.Is<DatosCatalogo>(d => d.Descripcion == "Signos vitales" && d.ClaseRiesgo == "III" && d.RequiereCalibracion == true
                && d.PeriodicidadCalibracionMeses == 6 && d.Activo == null),
            Arg.Any<CancellationToken>());
        Assert.Equal("Valores por defecto guardados", vm.Confirmacion!.Titulo);
    }

    [Fact]
    public async Task GuardarValores_SinCalibracion_EnviaPeriodicidadVaciaYClasePorDefinir()
    {
        var vm = await AbrirAsync("AC-02");
        _servicio.ObtenerAsync(TiposCatalogo.TiposEquipo, Monitor.Id, Arg.Any<CancellationToken>()).Returns(Monitor);
        vm.FilaSeleccionada = vm.Filas[0];
        vm.Clase = CatalogosViewModel.OpcionesClase[0];
        vm.RequiereCalibracion = false;

        await vm.GuardarValoresCommand.ExecuteAsync(null);

        await _servicio.Received(1).ActualizarAsync(
            TiposCatalogo.TiposEquipo,
            Monitor.Id,
            Arg.Is<DatosCatalogo>(d => d.ClaseRiesgo == null && d.RequiereCalibracion == false && d.PeriodicidadCalibracionMeses == null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("doce")]
    public async Task GuardarValores_PeriodicidadFueraDeRango_NoGuarda(string meses)
    {
        var vm = await AbrirAsync("AC-02");
        vm.FilaSeleccionada = vm.Filas[0];
        vm.Meses = meses;

        await vm.GuardarValoresCommand.ExecuteAsync(null);

        Assert.Equal("Usa un valor entre 1 y 60 meses.", vm.ErrorMeses);
        await _servicio.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default!, default);
    }

    // ---------- Navegación: UI-019 Configuración ----------

    [Theory]
    [InlineData("AC-01", true)]
    [InlineData("AC-02", false)]
    public void Fabrica_UI019_AbreConfiguracionConCatalogosCamposYMiInstitucionSoloParaAC01(string rol, bool conInstitucion)
    {
        var institucion = Substitute.For<IServicioInstitucion>();
        var medidor = new MedidorPlan(1, 10, 10, false, false);
        institucion.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(new Institucion(
            Guid.NewGuid(), "Clínica San Rafael", "900.123.456-7", 15, "Clínica", null, new ConsumoPlan(medidor, medidor, medidor, false)));
        var servicios = new ServiceCollection()
            .AddSingleton(EquiposPrueba.Sesion(rol))
            .AddSingleton(_servicio)
            .AddSingleton(institucion)
            .AddTransient<MiInstitucionViewModel>()
            .AddSingleton(Substitute.For<IServicioConfiguracionCampos>())
            .AddTransient<CatalogosViewModel>()
            .AddTransient<CamposDinamicosViewModel>()
            .BuildServiceProvider();

        var pantalla = Assert.IsType<ConfiguracionViewModel>(new FabricaPantallas(servicios).Crear("UI-019"));

        Assert.Equal(conInstitucion, pantalla.TieneMiInstitucion);
        Assert.Same(conInstitucion ? pantalla.MiInstitucion : pantalla.Catalogos, pantalla.Actual);
        pantalla.MostrarCatalogosCommand.Execute(null);
        Assert.Same(pantalla.Catalogos, pantalla.Actual);
        pantalla.MostrarCamposCommand.Execute(null);
        Assert.Same(pantalla.CamposDinamicos, pantalla.Actual);
        Assert.True(pantalla.EnCampos);
        _servicio.Received().ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>());
    }
}
