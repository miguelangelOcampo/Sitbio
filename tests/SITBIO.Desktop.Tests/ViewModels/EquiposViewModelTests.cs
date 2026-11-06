using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

public class EquiposViewModelTests
{
    private readonly IServicioEquipos _servicio = Substitute.For<IServicioEquipos>();
    private readonly IServicioCatalogos _catalogos = Substitute.For<IServicioCatalogos>();

    private EquiposViewModel Crear(PaginaEquipos? pagina = null)
    {
        _servicio.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(pagina ?? Pagina(0));
        return new EquiposViewModel(_servicio, _catalogos, Sesion("AC-03"), new Reloj());
    }

    private FiltroEquipos UltimoFiltro() =>
        (FiltroEquipos)_servicio.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(IServicioEquipos.ListarAsync)).GetArguments()[0]!;

    [Fact]
    public async Task Cargar_MuestraLasFilasYElResumen()
    {
        var pantalla = Crear(Pagina(45, Equipo(), Equipo("HSV-EQ-0413")));

        await pantalla.CargarCommand.ExecuteAsync(null);

        Assert.Equal(2, pantalla.Filas.Count);
        Assert.Equal("45 equipos en total", pantalla.ResumenTotal);
        Assert.Equal("Mostrando 1–20 de 45", pantalla.ResumenPagina);
        Assert.True(pantalla.HaySiguiente);
        Assert.False(pantalla.HayAnterior);
        Assert.False(pantalla.SinEquipos);
        Assert.Equal(new FiltroEquipos(Pagina: 1, Tamano: 20), UltimoFiltro());
    }

    [Fact]
    public async Task SinResultados_MuestraElEstadoVacio()
    {
        var pantalla = Crear(Pagina(0));

        await pantalla.CargarCommand.ExecuteAsync(null);

        Assert.True(pantalla.SinEquipos);
        Assert.Equal(string.Empty, pantalla.ResumenPagina);
    }

    [Fact]
    public void BuscarPorSerie_EnviaLaSerieYNoLaPlaca()
    {
        var pantalla = Crear();

        pantalla.BuscarPor = EquiposViewModel.OpcionesBuscarPor[1];
        pantalla.Busqueda = "  MX450 ";

        Assert.Equal("MX450", UltimoFiltro().Serie);
        Assert.Null(UltimoFiltro().Placa);
        Assert.Equal("Buscar por número de serie", pantalla.TextoBusqueda);
        Assert.True(pantalla.HayFiltros);
    }

    [Fact]
    public void FiltroDeRiesgo_DistingueClaseYCalibracion()
    {
        var pantalla = Crear();

        pantalla.FiltroRiesgo = EquiposViewModel.OpcionesRiesgo.Single(o => o.Valor == "IIb");
        Assert.Equal("IIb", UltimoFiltro().Clase);
        Assert.Null(UltimoFiltro().Calibracion);

        pantalla.FiltroRiesgo = EquiposViewModel.OpcionesRiesgo.Single(o => o.Texto == "No requiere calibración");
        Assert.Null(UltimoFiltro().Clase);
        Assert.False(UltimoFiltro().Calibracion);
    }

    [Fact]
    public async Task CambiarUnFiltro_VuelveALaPrimeraPagina()
    {
        var pantalla = Crear(Pagina(45, Equipo()));
        await pantalla.PaginaSiguienteCommand.ExecuteAsync(null);
        Assert.Equal(2, pantalla.Pagina);
        Assert.Equal("Mostrando 21–40 de 45", pantalla.ResumenPagina);

        pantalla.FiltroEstado = EquiposViewModel.OpcionesEstado.Single(o => o.Valor == "EN_MANTENIMIENTO");

        Assert.Equal(1, pantalla.Pagina);
        Assert.Equal(new FiltroEquipos(Estado: "EN_MANTENIMIENTO", Pagina: 1, Tamano: 20), UltimoFiltro());
    }

    [Fact]
    public async Task Limpiar_QuitaTodosLosFiltrosConUnaSolaConsulta()
    {
        var pantalla = Crear();
        pantalla.Busqueda = "HSV";
        pantalla.FiltroEstado = EquiposViewModel.OpcionesEstado[1];
        _servicio.ClearReceivedCalls();

        await pantalla.LimpiarCommand.ExecuteAsync(null);

        await _servicio.Received(1).ListarAsync(new FiltroEquipos(Pagina: 1, Tamano: 20), Arg.Any<CancellationToken>());
        Assert.False(pantalla.HayFiltros);
    }

    [Fact]
    public async Task ErrorDelServidor_MuestraAvisoConReintentar()
    {
        var pantalla = Crear();
        _servicio.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Tu rol no permite consultar el inventario de equipos.", new Dictionary<string, string>()));

        await pantalla.CargarCommand.ExecuteAsync(null);

        Assert.Equal("No pudimos cargar el inventario", pantalla.Aviso!.Titulo);
        Assert.True(pantalla.Aviso.PuedeReintentar);
        Assert.Empty(pantalla.Filas);
        Assert.False(pantalla.SinEquipos);
    }

    [Fact]
    public async Task RespuestaVieja_NoReemplazaALaUltimaConsulta()
    {
        var lenta = new TaskCompletionSource<PaginaEquipos>();
        _servicio.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == null), Arg.Any<CancellationToken>()).Returns(lenta.Task);
        _servicio.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>())
            .Returns(Pagina(1, Equipo("HSV-EQ-0001")));
        var pantalla = new EquiposViewModel(_servicio, _catalogos, Sesion("AC-03"), new Reloj());

        var primera = pantalla.CargarCommand.ExecuteAsync(null);
        pantalla.Busqueda = "HSV";
        lenta.SetResult(Pagina(2, Equipo("VIEJA-1"), Equipo("VIEJA-2")));
        await primera;

        Assert.Equal("HSV-EQ-0001", Assert.Single(pantalla.Filas).Placa);
        Assert.False(pantalla.Cargando);
    }

    [Fact]
    public async Task AbrirYVolver_ConservaFiltrosYPagina()
    {
        var equipo = Equipo();
        var pantalla = Crear(Pagina(45, equipo));
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        pantalla.Busqueda = "HSV";
        await pantalla.PaginaSiguienteCommand.ExecuteAsync(null);
        _servicio.ClearReceivedCalls();

        await pantalla.AbrirCommand.ExecuteAsync(pantalla.Filas[0]);

        Assert.False(pantalla.EnLista);
        Assert.Equal("Monitor de signos vitales", pantalla.Detalle!.Titulo);

        pantalla.Detalle.VolverCommand.Execute(null);

        Assert.True(pantalla.EnLista);
        Assert.Equal("HSV", pantalla.Busqueda);
        Assert.Equal(2, pantalla.Pagina);
        await _servicio.DidNotReceive().ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(3, false, "en 3 d")]
    [InlineData(0, false, "hoy")]
    [InlineData(8, false, null)]
    [InlineData(-2, true, "Vencido")]
    public void Fila_AvisaElMantenimientoCercanoOVencido(int dias, bool vencido, string? esperado)
    {
        var fila = new FilaEquipo(Equipo(plan: Plan(Hoy.AddDays(dias), vencido)), Hoy);

        Assert.Equal(esperado, fila.DetalleMantenimiento);
    }

    [Fact]
    public void Fila_MuestraElAvisoDeRegistroYElTonoDelEstado()
    {
        var fila = new FilaEquipo(Equipo(estado: "FUERA_DE_SERVICIO", avisoRegistro: AvisosRegistro.Vencido), Hoy);

        Assert.Equal("Registro vencido", fila.AvisoRegistro);
        Assert.Equal(TonoEstado.Critico, fila.Tono);
        Assert.Equal("IIb · Riesgo alto", fila.DescripcionClase);
        Assert.Equal("—", fila.ProximoMantenimiento);
    }

    [Fact]
    public void Fila_DeEquipoDeBaja_NoAvisaRegistroNiMantenimiento()
    {
        var fila = new FilaEquipo(
            Equipo(estado: "BAJA", avisoRegistro: AvisosRegistro.Vencido, plan: Plan(Hoy, vencido: true), baja: DateTimeOffset.UtcNow),
            Hoy);

        Assert.Null(fila.AvisoRegistro);
        Assert.Null(fila.DetalleMantenimiento);
        Assert.Equal(TonoEstado.Neutro, fila.Tono);
    }

    // ---------- UI-007: registrar y editar (AC-02) ----------

    private static readonly ElementoCatalogo TipoMonitor = new(Guid.NewGuid(), "Monitor de signos vitales", true, "IIb", true, 12);
    private static readonly ElementoCatalogo AreaUci = new(Guid.NewGuid(), "UCI adultos", true);

    private EquiposViewModel ComoIngeniero(PaginaEquipos? pagina = null)
    {
        _servicio.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(pagina ?? Pagina(0));
        _catalogos.ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>()).Returns([TipoMonitor]);
        _catalogos.ListarAsync(TiposCatalogo.Ubicaciones, Arg.Any<CancellationToken>()).Returns([AreaUci]);
        _catalogos.ListarAsync(TiposCatalogo.Proveedores, Arg.Any<CancellationToken>()).Returns([]);
        return new EquiposViewModel(_servicio, _catalogos, Sesion("AC-02"), new Reloj());
    }

    private static void Llenar(FormularioEquipoViewModel formulario)
    {
        formulario.Placa = "CSR-EQ-0100";
        formulario.Serie = "SN-100";
        formulario.Tipo = TipoMonitor;
        formulario.Marca = "Philips";
        formulario.Modelo = "MX450";
        formulario.RegistroInvima = "INVIMA 2019DM-0019830";
        formulario.VenceRegistro = "31/12/2028";
        formulario.Ubicacion = AreaUci;
        formulario.UltimoPreventivo = "01/10/2026";
    }

    [Theory]
    [InlineData("AC-02", true)]
    [InlineData("AC-01", false)]
    [InlineData("AC-03", false)]
    public void SoloElIngenieroBiomedico_VeRegistrar(string rol, bool puede)
    {
        var pantalla = new EquiposViewModel(_servicio, _catalogos, Sesion(rol), new Reloj());

        Assert.Equal(puede, pantalla.PuedeGestionar);
    }

    [Fact]
    public async Task NuevoEquipo_CargaLosCatalogosYAbreElFormulario()
    {
        var pantalla = ComoIngeniero();

        await pantalla.NuevoEquipoCommand.ExecuteAsync(null);

        Assert.True(pantalla.Formulario!.EsNuevo);
        Assert.Equal([TipoMonitor], pantalla.Formulario.Tipos);
        Assert.Equal([AreaUci], pantalla.Formulario.Ubicaciones);
    }

    [Fact]
    public async Task SiNoCarganLosCatalogos_NoAbreElFormulario()
    {
        var pantalla = ComoIngeniero();
        _catalogos.ListarAsync(TiposCatalogo.Ubicaciones, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));

        await pantalla.NuevoEquipoCommand.ExecuteAsync(null);

        Assert.Null(pantalla.Formulario);
        Assert.Equal("MSG-011", pantalla.Aviso!.Codigo);
    }

    [Fact]
    public async Task Registrar_EnviaLosDatosCierraYConfirma()
    {
        var pantalla = ComoIngeniero();
        _servicio.CrearAsync(Arg.Any<DatosEquipo>(), Arg.Any<CancellationToken>()).Returns(Equipo("CSR-EQ-0100"));
        await pantalla.NuevoEquipoCommand.ExecuteAsync(null);
        Llenar(pantalla.Formulario!);
        _servicio.ClearReceivedCalls();

        await pantalla.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CrearAsync(
            Arg.Is<DatosEquipo>(d => d.PlacaInventario == "CSR-EQ-0100" && d.TipoEquipoId == TipoMonitor.Id && d.Preventivo!.PeriodicidadDias == 90),
            Arg.Any<CancellationToken>());
        Assert.Null(pantalla.Formulario);
        Assert.Equal("Equipo CSR-EQ-0100 registrado", pantalla.Confirmacion!.Titulo);
        await _servicio.Received(1).ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FormularioIncompleto_NoLlamaALaApi()
    {
        var pantalla = ComoIngeniero();
        await pantalla.NuevoEquipoCommand.ExecuteAsync(null);

        await pantalla.GuardarCommand.ExecuteAsync(null);

        await _servicio.DidNotReceive().CrearAsync(Arg.Any<DatosEquipo>(), Arg.Any<CancellationToken>());
        Assert.Equal("MSG-008", pantalla.Formulario!.Aviso!.Codigo);
    }

    [Fact]
    public async Task PlacaRepetida_DejaElFormularioAbiertoConElError()
    {
        var pantalla = ComoIngeniero();
        _servicio.CrearAsync(Arg.Any<DatosEquipo>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("RN-001", "Ya existe un equipo con la placa de inventario CSR-EQ-0100 en tu institución.", new Dictionary<string, string>()));
        await pantalla.NuevoEquipoCommand.ExecuteAsync(null);
        Llenar(pantalla.Formulario!);

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(pantalla.Formulario);
        Assert.Contains("CSR-EQ-0100", pantalla.Formulario!.ErrorPlaca);
        Assert.Null(pantalla.Confirmacion);
    }

    [Fact]
    public async Task EditarDesdeLaHojaDeVida_ActualizaElDetalle()
    {
        var equipo = Equipo() with { TipoEquipoId = TipoMonitor.Id, UbicacionId = AreaUci.Id };
        var pantalla = ComoIngeniero(Pagina(1, equipo));
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        _servicio.ActualizarAsync(equipo.Id, Arg.Any<DatosEquipo>(), Arg.Any<CancellationToken>()).Returns(equipo with { Modelo = "MX550" });
        await pantalla.CargarCommand.ExecuteAsync(null);
        await pantalla.AbrirCommand.ExecuteAsync(pantalla.Filas[0]);
        Assert.True(pantalla.Detalle!.PuedeEditar);

        await pantalla.EditarCommand.ExecuteAsync(pantalla.Detalle.Equipo);
        Assert.False(pantalla.Formulario!.EsNuevo);
        pantalla.Formulario.Modelo = "MX550";
        pantalla.Formulario.NoSeConoce = true;
        pantalla.Formulario.ProximoPreventivo = "01/12/2026";
        await pantalla.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).ActualizarAsync(equipo.Id, Arg.Is<DatosEquipo>(d => d.Modelo == "MX550"), Arg.Any<CancellationToken>());
        Assert.Null(pantalla.Formulario);
        Assert.Equal("MX550", pantalla.Detalle.Equipo!.Modelo);
        Assert.Equal("Cambios guardados", pantalla.Detalle.Confirmacion!.Titulo);
    }
}
