using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

public class EquiposViewModelTests
{
    private readonly IServicioEquipos _servicio = Substitute.For<IServicioEquipos>();

    private EquiposViewModel Crear(PaginaEquipos? pagina = null)
    {
        _servicio.ListarAsync(Arg.Any<FiltroEquipos>(), Arg.Any<CancellationToken>()).Returns(pagina ?? Pagina(0));
        return new EquiposViewModel(_servicio, new Reloj());
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
        var pantalla = new EquiposViewModel(_servicio, new Reloj());

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
}
