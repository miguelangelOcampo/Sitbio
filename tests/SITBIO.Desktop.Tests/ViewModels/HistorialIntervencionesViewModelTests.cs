using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// Historial de intervenciones (HU-005): consulta con filtros y totales, fechas dd/mm/aaaa, paginación, apertura
/// de la intervención en solo lectura según el rol y de la hoja de vida del equipo.
/// </summary>
public class HistorialIntervencionesViewModelTests
{
    private static readonly Guid TipoBomba = Guid.NewGuid();
    private readonly IServicioHistorialIntervenciones _historial = Substitute.For<IServicioHistorialIntervenciones>();
    private readonly IServicioCatalogos _catalogos = Substitute.For<IServicioCatalogos>();
    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly List<FiltroHistorial> _consultas = [];
    private IReadOnlyList<IntervencionHistorial> _items = [];
    private int _total;

    public HistorialIntervencionesViewModelTests()
    {
        _catalogos.ListarAsync(TiposCatalogo.TiposEquipo, Arg.Any<CancellationToken>())
            .Returns([new ElementoCatalogo(TipoBomba, "Bomba de infusión", true), new ElementoCatalogo(Guid.NewGuid(), "Autoclave", true)]);
        _historial.ConsultarAsync(Arg.Any<FiltroHistorial>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _consultas.Add(llamada.ArgAt<FiltroHistorial>(0));
            var resumen = _total == 0 ? new ResumenHistorial(0, 0, 0, 0, null) : new ResumenHistorial(_total, 3, _total - 3, 1250000m, 95);
            return new PaginaHistorial(_items, 1, HistorialIntervencionesViewModel.TamanoPagina, _total, resumen);
        });
    }

    private static IntervencionHistorial Intervencion(string placa = "BOM-05", string tipo = "correctivo", string? tiquete = "TQT-000031", bool firmada = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), placa, "Bomba de infusión", Guid.NewGuid(), "Carlos Pérez", tipo,
            "Sensor de oclusión descalibrado", 185000m, 90, "Operativo", firmada, new DateTimeOffset(2026, 11, 14, 20, 30, 0, TimeSpan.Zero), tiquete);

    private async Task<HistorialIntervencionesViewModel> AbrirAsync(string rol = "AC-02", int total = 1, params IntervencionHistorial[] items)
    {
        _items = items.Length > 0 ? items : [Intervencion()];
        _total = total;
        var vm = new HistorialIntervencionesViewModel(
            _historial, _catalogos, _ordenes, Substitute.For<IServicioEquipos>(), Substitute.For<IServicioConfiguracionCampos>(), EquiposPrueba.Sesion(rol));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Cargar_ConsultaLaPrimeraPaginaSinFiltros_YMuestraFilasYTotales()
    {
        var vm = await AbrirAsync(total: 8);

        var consulta = Assert.Single(_consultas);
        Assert.Equal(new FiltroHistorial(Pagina: 1, Tamano: HistorialIntervencionesViewModel.TamanoPagina), consulta);
        Assert.Equal(["Todos los tipos", "Autoclave", "Bomba de infusión"], vm.OpcionesTipo.Select(o => o.Texto));

        var fila = Assert.Single(vm.Filas);
        Assert.Equal(("14/11/2026", "15:30"), (fila.Fecha, fila.Hora));
        Assert.Equal(("BOM-05 · Bomba de infusión", "TQT-000031", "Correctivo"), (fila.Equipo, fila.Referencia, fila.Tipo));
        Assert.Equal(("1 h 30 min", "Operativo", "Aprobada"), (fila.Tiempo, fila.Salida, fila.Firma));
        Assert.Equal(185000m.ToString("C0", TextosEquipo.Cultura), fila.Costo);

        Assert.Equal(("8", "3", "5 preventivas"), (vm.Intervenciones, vm.Correctivas, vm.DetalleCorrectivas));
        Assert.Equal(1250000m.ToString("C0", TextosEquipo.Cultura), vm.CostoTotal);
        Assert.Equal("1 h 35 min", vm.TiempoPromedio);
        Assert.Equal("1–8 de 8 intervenciones", vm.ResumenPagina);
    }

    [Fact]
    public async Task Filtros_ArmanLaConsulta_YLimpiarVuelveATodo()
    {
        var vm = await AbrirAsync();

        vm.FiltroTipo = vm.OpcionesTipo.Single(o => o.Texto == "Bomba de infusión");
        vm.FiltroMantenimiento = HistorialIntervencionesViewModel.OpcionesMantenimiento[2];
        vm.Desde = "01/11/2026";
        vm.Hasta = "30/11/2026";
        vm.TextoEquipo = "  BOM ";
        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(
            new FiltroHistorial("BOM", TipoBomba, null, "correctivo", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, HistorialIntervencionesViewModel.TamanoPagina),
            _consultas[^1]);
        Assert.True(vm.HayFiltros);

        await vm.LimpiarCommand.ExecuteAsync(null);

        Assert.Equal(new FiltroHistorial(Pagina: 1, Tamano: HistorialIntervencionesViewModel.TamanoPagina), _consultas[^1]);
        Assert.False(vm.HayFiltros);
    }

    [Theory]
    [InlineData("2026-11-31x", "", "Escribe la fecha como dd/mm/aaaa, por ejemplo 05/11/2026.")]
    [InlineData("30/11/2026", "01/11/2026", "La fecha final no puede ser anterior a la inicial.")]
    public async Task FechasInvalidas_NoConsultan_YExplicanElError(string desde, string hasta, string error)
    {
        var vm = await AbrirAsync();
        vm.Desde = desde;
        var consultas = _consultas.Count;

        vm.Hasta = hasta;
        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(error, vm.ErrorFechas);
        Assert.Equal(consultas, _consultas.Count);
    }

    [Fact]
    public async Task Paginacion_PideLaSiguientePagina()
    {
        var vm = await AbrirAsync(total: 45);
        Assert.True(vm.HaySiguiente);

        await vm.PaginaSiguienteCommand.ExecuteAsync(null);

        Assert.Equal(2, _consultas[^1].Pagina);
        Assert.Equal(2, vm.Pagina);
    }

    [Fact]
    public async Task SinResultados_MuestraElEstadoVacio()
    {
        _items = [];
        var vm = new HistorialIntervencionesViewModel(
            _historial, _catalogos, _ordenes, Substitute.For<IServicioEquipos>(), Substitute.For<IServicioConfiguracionCampos>(), EquiposPrueba.Sesion("AC-02"));
        _total = 0;

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinIntervenciones);
        Assert.Equal("0 intervenciones", vm.ResumenPagina);
        Assert.Equal(("0", "—"), (vm.Intervenciones, vm.TiempoPromedio));
    }

    [Fact]
    public async Task ErrorDeLaApi_MuestraElAvisoConReintentar()
    {
        _historial.ConsultarAsync(Arg.Any<FiltroHistorial>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Tu rol no permite consultar el historial de intervenciones.", new Dictionary<string, string>()));
        var vm = new HistorialIntervencionesViewModel(
            _historial, _catalogos, _ordenes, Substitute.For<IServicioEquipos>(), Substitute.For<IServicioConfiguracionCampos>(), EquiposPrueba.Sesion("AC-02"));

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("No se pudo consultar el historial", vm.Aviso?.Titulo);
        Assert.True(vm.Aviso?.PuedeReintentar);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task HojaDeVida_PideAbrirElEquipoEnEquiposBiomedicos()
    {
        var intervencion = Intervencion();
        var vm = await AbrirAsync(items: intervencion);
        SolicitudNavegacion? solicitud = null;
        vm.NavegacionSolicitada += (_, s) => solicitud = s;

        vm.AbrirHojaVidaCommand.Execute(vm.Filas[0]);

        Assert.Equal(new SolicitudNavegacion("UI-005", EquiposViewModel.ParametroDetalle(intervencion.EquipoId, "BOM-05")), solicitud);
    }

    [Theory]
    [InlineData("AC-01", true)]
    [InlineData("AC-02", true)]
    [InlineData("AC-03", true)]
    [InlineData("AC-05", false)]
    [InlineData("AC-07", true)]
    public async Task VerIntervencion_SoloParaLosRolesQueConsultanOrdenes(string rol, bool puede)
    {
        var vm = await AbrirAsync(rol);

        Assert.Equal(puede, vm.PuedeVerIntervencion);
    }

    [Fact]
    public async Task VerIntervencion_AbreUi013EnSoloLectura_YAlVolverQuedaLaLista()
    {
        var intervencion = Intervencion();
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.Aprobada, intervencion: true) with { Id = intervencion.OrdenId };
        _ordenes.ObtenerAsync(intervencion.OrdenId, Arg.Any<CancellationToken>()).Returns(orden);
        var vm = await AbrirAsync(items: intervencion);

        await vm.AbrirIntervencionCommand.ExecuteAsync(vm.Filas[0]);

        Assert.NotNull(vm.Intervencion);
        Assert.False(vm.Intervencion!.Editable);
        Assert.False(vm.EnLista);

        vm.Intervencion.CancelarCommand.Execute(null);

        Assert.True(vm.EnLista);
    }

    [Fact]
    public async Task VerIntervencion_OrdenDeOtroTecnico_AvisaYSugiereLaHojaDeVida()
    {
        var intervencion = Intervencion();
        _ordenes.ObtenerAsync(intervencion.OrdenId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "La orden ya no está disponible para ti. Actualiza la lista.", new Dictionary<string, string>()));
        var vm = await AbrirAsync("AC-03", items: intervencion);

        await vm.AbrirIntervencionCommand.ExecuteAsync(vm.Filas[0]);

        Assert.Null(vm.Intervencion);
        Assert.Equal("No se pudo abrir la intervención de BOM-05 · Bomba de infusión", vm.Aviso?.Titulo);
        Assert.Contains("hoja de vida", vm.Aviso?.Mensaje);
    }

    [Fact]
    public void Ruta_LlevaSoloLosFiltrosPresentes()
    {
        Assert.Equal("intervenciones?page=1&pageSize=20", ServicioHistorialIntervencionesHttp.Ruta(new FiltroHistorial()));
        Assert.Equal(
            $"intervenciones?page=2&pageSize=20&equipo=BOM%2005&tipoEquipoId={TipoBomba}&tipoMantenimiento=preventivo&desde=2026-11-01&hasta=2026-11-30",
            ServicioHistorialIntervencionesHttp.Ruta(new FiltroHistorial(" BOM 05 ", TipoBomba, null, "preventivo", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 2)));
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(95.4, "1 h 35 min")]
    public void Duracion_EnHorasYMinutos(double minutos, string texto) =>
        Assert.Equal(texto, TextosHistorial.Duracion(minutos));
}
