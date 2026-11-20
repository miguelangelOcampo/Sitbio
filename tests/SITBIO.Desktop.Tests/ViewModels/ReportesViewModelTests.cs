using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-021 (HU-026) y UI-022 (HU-007, HU-027).</summary>
public class ReportesViewModelTests
{
    private readonly IServicioReportes _servicio = Substitute.For<IServicioReportes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static Indicadores Kpi(bool costos = true) => new(
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
        new CumplimientoPreventivo(8, 6, 75), 6, 2880, new CostoPorEquipo(costos ? 500000m : null, 20, costos ? 25000m : null), new FueraDeServicio(2, 20, 10), costos);

    private ExportacionViewModel Exportacion(string rol, TimeSpan? retardo = null)
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("token", DateTimeOffset.UtcNow.AddHours(1), rol, false));

        // Sin coincidencias salvo lo que cada prueba configure; sin retardo para no esperar en las pruebas.
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(Pagina(0));
        return new ExportacionViewModel(_servicio, _equipos, _dialogo, sesion, retardo ?? TimeSpan.Zero);
    }

    // ---------- UI-021 ----------

    [Fact]
    public async Task Tablero_MuestraLosCincoIndicadores()
    {
        _servicio.IndicadoresAsync(null, null, Arg.Any<CancellationToken>()).Returns(Kpi());
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("75 %", "6 de 8 preventivas cerradas", "6 h", "2880 h"), (vm.Cumplimiento, vm.DetalleCumplimiento, vm.Mttr, vm.Mtbf));
        Assert.Equal(("2", "10 % de 20 equipos activos"), (vm.Fuera, vm.DetalleFuera));
        Assert.Contains("25.000", vm.Costo);
        Assert.Equal("Del 01/11/2026 al 30/11/2026", vm.Periodo);
        Assert.Null(vm.AvisoCostos);
    }

    [Fact]
    public async Task Tablero_SinCostos_LoAvisa_YFechaInvalidaNoConsulta()
    {
        _servicio.IndicadoresAsync(default, default, default).ReturnsForAnyArgs(Kpi(costos: false));
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);
        Assert.Equal(("—", "Costo no disponible"), (vm.Costo, vm.DetalleCosto));
        Assert.NotNull(vm.AvisoCostos);

        vm.Desde = "31-13-2026";
        await vm.CargarCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorFechas);
        await _servicio.ReceivedWithAnyArgs(1).IndicadoresAsync(default, default, default);
    }

    [Fact]
    public async Task Tablero_ErrorDeLaApi_MuestraAviso()
    {
        _servicio.IndicadoresAsync(default, default, default).ThrowsAsyncForAnyArgs(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = new ReportesViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("Sin conexión", vm.Aviso?.Titulo);
        Assert.False(vm.HayDatos);
    }

    // ---------- UI-021 gráficos (I-02) ----------

    private static IReadOnlyList<MesIndicadores> Meses(int cantidad) => Enumerable.Range(0, cantidad)
        .Select(i => new MesIndicadores(new DateOnly(2026, 11, 1).AddMonths(i - cantidad + 1), 3 + i, i % 2, i == 0 ? null : 80, 4))
        .ToList();

    private static readonly Guid Bomba = Guid.NewGuid();

    private static Parque ParqueDe(params EquipoFuera[] fuera) => new(6, 2, fuera.Length, fuera);

    private ReportesViewModel ConGraficos(string? rol = null)
    {
        _servicio.IndicadoresAsync(default, default, default).ReturnsForAnyArgs(Kpi());
        _servicio.SerieIndicadoresAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(c => Meses(c.Arg<int>()));
        _servicio.ParqueAsync(Arg.Any<CancellationToken>()).Returns(ParqueDe(new EquipoFuera(Bomba, "HDS-0014", "Bomba de infusión", "UCI · Cama 3")));
        if (rol is null)
        {
            return new ReportesViewModel(_servicio);
        }

        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("token", DateTimeOffset.UtcNow.AddHours(1), rol, false));
        return new ReportesViewModel(_servicio, sesion);
    }

    [Fact]
    public async Task Graficos_CarganSeisMeses_ConPreventivosCorrectivosYTabla()
    {
        var vm = ConGraficos();

        await vm.CargarCommand.ExecuteAsync(null);

        await _servicio.Received(1).SerieIndicadoresAsync(6, Arg.Any<CancellationToken>());
        Assert.True(vm.Meses6 && vm.VistaGrafico && vm.HaySerie);
        Assert.Equal(("jun", "nov", 6), (vm.EtiquetasSerie[0], vm.EtiquetasSerie[^1], vm.EtiquetasSerie.Count));
        Assert.Equal(["Preventivos", "Correctivos"], vm.SeriesMantenimientos.Select(x => x.Nombre));
        Assert.Equal([3d, 4, 5, 6, 7, 8], vm.SeriesMantenimientos[0].Valores);
        Assert.Equal(new FilaMes("Noviembre 2026", 8, 1, 9), vm.FilasSerie[^1]);
        Assert.Equal(0, vm.MiniCumplimiento[0].Valores[0]);
        Assert.Equal(6, vm.MiniMttr[0].Valores.Count);
        Assert.Equal([18d, 2], vm.MiniDisponibilidad.Select(x => x.Valores[0]));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    public async Task Graficos_ElegirOtroPeriodo_RecargaLaSerie(int meses)
    {
        var vm = ConGraficos();
        await vm.CargarCommand.ExecuteAsync(null);

        if (meses == 3)
        {
            vm.Meses3 = true;
        }
        else
        {
            vm.Meses12 = true;
        }

        await _servicio.Received(1).SerieIndicadoresAsync(meses, Arg.Any<CancellationToken>());
        Assert.Equal(meses, vm.FilasSerie.Count);
        Assert.False(vm.Meses6);

        vm.VistaTabla = true;
        Assert.False(vm.VistaGrafico);
    }

    [Fact]
    public async Task EstadoDelParque_MuestraLosTresEstados_YLosEquiposFuera()
    {
        var vm = ConGraficos();

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.HayParque);
        Assert.Equal((6, 2, 1), (vm.Operativos, vm.EnMantenimiento, vm.FueraDeServicio));
        Assert.Equal(["Operativo", "En mantenimiento", "Fuera de servicio"], vm.SeriesParque.Select(x => x.Nombre));
        Assert.Equal("67 %", vm.TextoOperativos);
        Assert.Equal("HDS-0014", Assert.Single(vm.EquiposFuera).PlacaInventario);
        Assert.False(vm.SinEquiposFuera);
    }

    [Fact]
    public async Task EstadoDelParque_SinEquiposFuera_LoDice()
    {
        var vm = ConGraficos();
        _servicio.ParqueAsync(Arg.Any<CancellationToken>()).Returns(ParqueDe());

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinEquiposFuera);
        Assert.Empty(vm.EquiposFuera);
    }

    [Fact]
    public async Task EquipoFuera_AbreSuHojaDeVida_SoloSiElRolTieneEquipos()
    {
        var gestion = ConGraficos("AC-02");
        await gestion.CargarCommand.ExecuteAsync(null);
        SolicitudNavegacion? solicitud = null;
        gestion.NavegacionSolicitada += (_, s) => solicitud = s;

        gestion.AbrirEquipoCommand.Execute(gestion.EquiposFuera[0]);

        Assert.True(gestion.PuedeAbrirEquipos);
        Assert.Equal(new SolicitudNavegacion(EquiposViewModel.IdPantalla, EquiposViewModel.ParametroDetalle(Bomba, "HDS-0014")), solicitud);

        var directiva = ConGraficos("AC-06");
        await directiva.CargarCommand.ExecuteAsync(null);
        var navego = false;
        directiva.NavegacionSolicitada += (_, _) => navego = true;

        directiva.AbrirEquipoCommand.Execute(directiva.EquiposFuera[0]);

        Assert.False(directiva.PuedeAbrirEquipos);
        Assert.False(navego);
    }

    [Fact]
    public async Task Graficos_ErrorDeLaApi_MuestraAvisoSinOcultarLosIndicadores()
    {
        var vm = ConGraficos();
        _servicio.SerieIndicadoresAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("ERR", "Falló la consulta", new Dictionary<string, string>()));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("No pudimos cargar los gráficos", vm.AvisoGraficos?.Titulo);
        Assert.True(vm.HayDatos);
        Assert.False(vm.HaySerie);
    }

    // ---------- UI-022 ----------

    [Fact]
    public async Task Exportar_DescargaElFormatoElegido_YLoGuardaDondeDiga()
    {
        var vm = Exportacion("AC-05");
        vm.Desde = "01/11/2026";
        vm.Hasta = "30/11/2026";
        vm.Formato = ExportacionViewModel.Formatos[1];
        _servicio.HistorialOrdenesAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "xlsx", Arg.Any<CancellationToken>())
            .Returns(new ArchivoDescargado([1, 2], "historial.xlsx"));
        _dialogo.ElegirDestino("historial.xlsx").Returns(@"C:\Reportes\historial.xlsx");

        await vm.ExportarHistorialCommand.ExecuteAsync(null);

        _dialogo.Received(1).Guardar(@"C:\Reportes\historial.xlsx", Arg.Is<byte[]>(b => b.Length == 2));
        Assert.Equal(("Archivo guardado", @"C:\Reportes\historial.xlsx"), (vm.Confirmacion?.Titulo, vm.Confirmacion?.Mensaje));
        Assert.True(vm.PuedeExportarHistorial);
    }

    [Fact]
    public async Task Exportar_CanceladoOFechasInvalidas_NoGuarda()
    {
        var vm = Exportacion("AC-02");
        vm.Desde = "30/11/2026";
        vm.Hasta = "01/11/2026";
        await vm.ExportarHistorialCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorFechas);

        vm.Desde = "01/11/2026";
        vm.Hasta = "30/11/2026";
        _servicio.HistorialOrdenesAsync(default, default, default!, default).ReturnsForAnyArgs(new ArchivoDescargado([1], "h.pdf"));
        _dialogo.ElegirDestino("h.pdf").Returns((string?)null);
        await vm.ExportarHistorialCommand.ExecuteAsync(null);

        _dialogo.DidNotReceiveWithAnyArgs().Guardar(default!, default!);
        Assert.Null(vm.Confirmacion);
    }

    [Fact]
    public async Task HojaDeVida_ElAuditorBuscaElEquipo_YGeneraElPdf()
    {
        var vm = Exportacion("AC-07");
        var equipo = Equipo();
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>()).Returns(new PaginaEquipos([equipo], 1, 10, 1));
        _servicio.HojaVidaAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado([9], "hoja-de-vida.pdf"));
        _dialogo.ElegirDestino("hoja-de-vida.pdf").Returns(@"C:\hv.pdf");

        await vm.GenerarHojaVidaCommand.ExecuteAsync(null);
        Assert.Equal("Elige un equipo", vm.Aviso?.Titulo);

        vm.Placa = "HSV";
        await vm.BusquedaEnCurso;
        Assert.Equal(equipo, vm.Sugerido);
        await vm.ElegirEquipoCommand.ExecuteAsync(null);
        await vm.GenerarHojaVidaCommand.ExecuteAsync(null);

        Assert.False(vm.PuedeExportarHistorial);
        Assert.Equal(equipo, vm.EquipoElegido);
        Assert.Equal(equipo.PlacaInventario, vm.Placa);
        Assert.False(vm.HaySugerencias);
        _dialogo.Received(1).Guardar(@"C:\hv.pdf", Arg.Any<byte[]>());
    }

    // ---------- I-08: búsqueda incremental del equipo ----------

    [Fact]
    public async Task HojaDeVida_SugierePorPlacaSerieONombre_SinRepetir()
    {
        var vm = Exportacion("AC-02");
        var porPlaca = Equipo("HSV-EQ-0412");
        var porNombre = Equipo("HSV-EQ-0007");
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "mon"), Arg.Any<CancellationToken>()).Returns(Pagina(1, porPlaca));
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Serie == "mon"), Arg.Any<CancellationToken>()).Returns(Pagina(1, porPlaca));
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Texto == "mon"), Arg.Any<CancellationToken>()).Returns(Pagina(2, porPlaca, porNombre));

        vm.Placa = " mon ";
        await vm.BusquedaEnCurso;

        Assert.Equal([porPlaca, porNombre], vm.Resultados);
        Assert.Equal(porPlaca, vm.Sugerido);
        Assert.True(vm.HaySugerencias);
        await _equipos.Received(1).ListarAsync(Arg.Is<FiltroEquipos>(f => f.Texto == "mon" && f.Tamano == ExportacionViewModel.MaximoSugerencias), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HojaDeVida_ConUnCaracter_NoConsulta()
    {
        var vm = Exportacion("AC-02");

        vm.Placa = "H";
        await vm.BusquedaEnCurso;

        Assert.Null(vm.Resultados);
        Assert.False(vm.SinResultados);
        await _equipos.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
    }

    [Fact]
    public async Task HojaDeVida_SinCoincidencias_LoDice_YNoHaySugerencias()
    {
        var vm = Exportacion("AC-02");

        vm.Placa = "zzz";
        await vm.BusquedaEnCurso;

        Assert.True(vm.SinResultados);
        Assert.False(vm.HaySugerencias);
        Assert.Null(vm.Sugerido);
    }

    [Fact]
    public async Task HojaDeVida_EsperaQueSeDejeDeEscribir_YSoloConsultaLoUltimo()
    {
        var vm = Exportacion("AC-02", TimeSpan.FromMilliseconds(60));

        vm.Placa = "HS";
        vm.Placa = "HSV";
        vm.Placa = "HSV-";
        await vm.BusquedaEnCurso;

        await _equipos.DidNotReceive().ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HS" || f.Placa == "HSV"), Arg.Any<CancellationToken>());
        await _equipos.Received(1).ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV-"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HojaDeVida_LasFlechasRecorrenLasSugerencias_YEnterEligeLaResaltada()
    {
        var vm = Exportacion("AC-02");
        var a = Equipo("HSV-EQ-0001");
        var b = Equipo("HSV-EQ-0002");
        var c = Equipo("HSV-EQ-0003");
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>()).Returns(Pagina(3, a, b, c));
        vm.Placa = "HSV";
        await vm.BusquedaEnCurso;

        vm.SugerenciaSiguienteCommand.Execute(null);
        vm.SugerenciaSiguienteCommand.Execute(null);
        Assert.Equal(c, vm.Sugerido);
        vm.SugerenciaSiguienteCommand.Execute(null);
        Assert.Equal(a, vm.Sugerido);
        vm.SugerenciaAnteriorCommand.Execute(null);
        Assert.Equal(c, vm.Sugerido);
        vm.SugerenciaAnteriorCommand.Execute(null);

        await vm.ElegirEquipoCommand.ExecuteAsync(null);

        Assert.Equal(b, vm.EquipoElegido);
        Assert.Equal("HSV-EQ-0002", vm.Placa);
        Assert.Null(vm.Resultados);
    }

    [Fact]
    public async Task HojaDeVida_ClicEnUnaSugerencia_LaElige_YEscribirDeNuevoLaDescarta()
    {
        var vm = Exportacion("AC-02");
        var a = Equipo("HSV-EQ-0001");
        var b = Equipo("HSV-EQ-0002");
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>()).Returns(Pagina(2, a, b));
        vm.Placa = "HSV";
        await vm.BusquedaEnCurso;

        await vm.ElegirEquipoCommand.ExecuteAsync(b);
        Assert.Equal(b, vm.EquipoElegido);

        vm.Placa = "HSV-EQ-000";
        await vm.BusquedaEnCurso;
        Assert.Null(vm.EquipoElegido);
    }

    [Fact]
    public async Task HojaDeVida_EnterAntesDeLasSugerencias_BuscaYEligeSiHayUnaSola()
    {
        var vm = Exportacion("AC-02", TimeSpan.FromSeconds(30));
        var equipo = Equipo();
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV-EQ-0412"), Arg.Any<CancellationToken>()).Returns(Pagina(1, equipo));

        vm.Placa = "HSV-EQ-0412";
        await vm.ElegirEquipoCommand.ExecuteAsync(null);

        Assert.Equal(equipo, vm.EquipoElegido);
        Assert.False(vm.BuscandoEquipo);
    }

    [Fact]
    public async Task HojaDeVida_EscCierraLasSugerencias_SinElegir()
    {
        var vm = Exportacion("AC-02");
        _equipos.ListarAsync(Arg.Is<FiltroEquipos>(f => f.Placa == "HSV"), Arg.Any<CancellationToken>()).Returns(Pagina(1, Equipo()));
        vm.Placa = "HSV";
        await vm.BusquedaEnCurso;

        vm.CerrarSugerenciasCommand.Execute(null);

        Assert.False(vm.HaySugerencias);
        Assert.Null(vm.EquipoElegido);
        Assert.Equal("HSV", vm.Placa);
    }

    [Fact]
    public async Task HojaDeVida_ErrorAlBuscar_MuestraAvisoConReintento()
    {
        var vm = Exportacion("AC-02");
        _equipos.ListarAsync(default!, default).ThrowsAsyncForAnyArgs(new ErrorApi(null, "Sin red", new Dictionary<string, string>(), true));

        vm.Placa = "HSV";
        await vm.BusquedaEnCurso;

        Assert.Equal("Sin conexión", vm.Aviso?.Titulo);
        Assert.True(vm.Aviso?.PuedeReintentar);
        Assert.False(vm.BuscandoEquipo);
    }
}
