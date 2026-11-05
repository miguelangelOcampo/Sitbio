using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-024 (HU-010, CA-HU-010-03): la pantalla consulta la API con los filtros y muestra su respuesta.</summary>
public class BitacoraViewModelTests
{
    private static readonly Guid Laura = Guid.NewGuid();
    private static readonly Guid Mateo = Guid.NewGuid();

    private readonly IServicioBitacora _servicio = Substitute.For<IServicioBitacora>();

    private static EventoBitacora Evento(Guid usuario, string? nombre, string accion, IReadOnlyDictionary<string, string>? detalle = null, string? ip = "10.0.0.5") =>
        new(Guid.NewGuid(), new DateTimeOffset(2026, 11, 5, 20, 30, 15, TimeSpan.Zero), usuario, nombre, accion, "usuarios", null, ip, detalle);

    private static PaginaBitacora Pagina(int total, params EventoBitacora[] eventos) => new(eventos, 1, BitacoraViewModel.TamanoPagina, total);

    /// <summary>Responde la consulta principal con <paramref name="principal"/> y los conteos de los indicadores.</summary>
    private void Responder(PaginaBitacora principal, int denegados = 0, int fallidos = 0)
    {
        _servicio.ConsultarAsync(Arg.Any<FiltroConsultaBitacora>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            var filtro = llamada.Arg<FiltroConsultaBitacora>();
            if (filtro.Tamano == 1)
            {
                return Pagina(filtro.Accion == CatalogoAcciones.AccesoDenegado ? denegados : fallidos);
            }

            return principal with { Page = filtro.Pagina };
        });
    }

    private List<FiltroConsultaBitacora> ConsultasPrincipales() =>
        _servicio.ReceivedCalls()
            .Select(c => (FiltroConsultaBitacora)c.GetArguments()[0]!)
            .Where(f => f.Tamano == BitacoraViewModel.TamanoPagina)
            .ToList();

    private async Task<BitacoraViewModel> Pantalla()
    {
        var pantalla = new BitacoraViewModel(_servicio);
        await pantalla.CargarCommand.ExecuteAsync(null);
        return pantalla;
    }

    [Fact]
    public async Task Cargar_MuestraLosEventosYLosIndicadores()
    {
        Responder(Pagina(3,
            Evento(Laura, "Laura Gómez", "ACCESO_DENEGADO", new Dictionary<string, string> { ["metodo"] = "POST", ["ruta"] = "/api/v1/usuarios", ["rol"] = "AC-02" }),
            Evento(Mateo, "Mateo Ruiz", "LOGIN_FALLIDO", new Dictionary<string, string> { ["motivo"] = "credenciales" }),
            Evento(Laura, "Laura Gómez", "CIERRE_SESION", new Dictionary<string, string> { ["motivo"] = "inactividad" }, ip: null)), denegados: 4, fallidos: 2);

        var pantalla = await Pantalla();

        Assert.Equal(3, pantalla.Total);
        Assert.Equal(4, pantalla.Denegados);
        Assert.Equal(2, pantalla.Fallidos);
        Assert.Equal(
            ["POST /api/v1/usuarios · rol AC-02", "Credenciales incorrectas", "Cierre por inactividad"],
            pantalla.Filas.Select(f => f.Detalle));
        Assert.Equal(["Acceso denegado", "Inicio de sesión fallido", "Cierre de sesión"], pantalla.Filas.Select(f => f.Accion));
        Assert.Equal([TonoAccion.Critico, TonoAccion.Critico, TonoAccion.Neutro], pantalla.Filas.Select(f => f.Tono));
        Assert.Equal("—", pantalla.Filas[2].Ip);
        Assert.Equal("1–3 de 3 eventos", pantalla.ResumenPagina);
        Assert.False(pantalla.SinEventos);
    }

    [Fact]
    public async Task LosUsuariosVistosQuedanComoOpcionesDelFiltro_OrdenadosPorNombre()
    {
        Responder(Pagina(3, Evento(Mateo, "Mateo Ruiz", "LOGIN_OK"), Evento(Laura, "Laura Gómez", "LOGIN_OK"), Evento(Mateo, "Mateo Ruiz", "CIERRE_SESION")));

        var pantalla = await Pantalla();

        Assert.Equal(["Todos los usuarios", "Laura Gómez", "Mateo Ruiz"], pantalla.OpcionesUsuario.Select(o => o.Texto));
    }

    [Fact]
    public async Task CA_HU_010_03_FiltrarPorUsuarioYFecha_ConsultaLaApiConEsosFiltros()
    {
        Responder(Pagina(1, Evento(Laura, "Laura Gómez", "LOGIN_OK")));
        var pantalla = await Pantalla();

        pantalla.FiltroUsuario = pantalla.OpcionesUsuario.Single(o => o.Texto == "Laura Gómez");
        pantalla.FiltroAccion = BitacoraViewModel.OpcionesAccion.Single(o => o.Valor == "LOGIN_OK");
        pantalla.Desde = "01/11/2026";
        pantalla.Hasta = "05/11/2026";

        var ultima = ConsultasPrincipales().Last();
        Assert.Equal(Laura, ultima.UsuarioId);
        Assert.Equal("LOGIN_OK", ultima.Accion);
        Assert.Equal(new DateTime(2026, 11, 1), ultima.Desde!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 11, 6), ultima.Hasta!.Value.DateTime); // «Hasta» incluye el 5 completo
        Assert.Equal(1, ultima.Pagina);
        Assert.Null(pantalla.ErrorFechas);
    }

    [Fact]
    public async Task FechaMalEscrita_AvisaYNoConsulta()
    {
        Responder(Pagina(0));
        var pantalla = await Pantalla();
        var antes = ConsultasPrincipales().Count;

        pantalla.Desde = "2026/31/11";

        Assert.Equal(antes, ConsultasPrincipales().Count);
        Assert.Contains("dd/mm/aaaa", pantalla.ErrorFechas);
    }

    [Fact]
    public async Task FechaFinalAnteriorALaInicial_AvisaYNoConsulta()
    {
        Responder(Pagina(0));
        var pantalla = await Pantalla();
        pantalla.Desde = "05/11/2026";
        var antes = ConsultasPrincipales().Count;

        pantalla.Hasta = "01/11/2026";

        Assert.Equal(antes, ConsultasPrincipales().Count);
        Assert.Equal("La fecha final no puede ser anterior a la inicial.", pantalla.ErrorFechas);
    }

    [Fact]
    public async Task Paginacion_AvanzaYRetrocedeSegunElTotal()
    {
        Responder(Pagina(120, Evento(Laura, "Laura Gómez", "LOGIN_OK")));
        var pantalla = await Pantalla();

        Assert.False(pantalla.PaginaAnteriorCommand.CanExecute(null));
        Assert.True(pantalla.PaginaSiguienteCommand.CanExecute(null));

        await pantalla.PaginaSiguienteCommand.ExecuteAsync(null);
        await pantalla.PaginaSiguienteCommand.ExecuteAsync(null);

        Assert.Equal(3, pantalla.Pagina);
        Assert.Equal("101–120 de 120 eventos", pantalla.ResumenPagina);
        Assert.False(pantalla.PaginaSiguienteCommand.CanExecute(null));
        Assert.Equal(3, ConsultasPrincipales().Last().Pagina);

        await pantalla.PaginaAnteriorCommand.ExecuteAsync(null);
        Assert.Equal(2, pantalla.Pagina);
    }

    [Fact]
    public async Task Limpiar_QuitaLosFiltrosYConsultaUnaSolaVez()
    {
        Responder(Pagina(1, Evento(Laura, "Laura Gómez", "LOGIN_OK")));
        var pantalla = await Pantalla();
        pantalla.FiltroAccion = BitacoraViewModel.OpcionesAccion[1];
        pantalla.Desde = "01/11/2026";
        Assert.True(pantalla.HayFiltros);
        var antes = ConsultasPrincipales().Count;

        await pantalla.LimpiarCommand.ExecuteAsync(null);

        Assert.False(pantalla.HayFiltros);
        Assert.Equal(antes + 1, ConsultasPrincipales().Count);
        Assert.Equal(new FiltroConsultaBitacora(Tamano: BitacoraViewModel.TamanoPagina), ConsultasPrincipales().Last());
    }

    [Fact]
    public async Task SinResultados_MuestraElEstadoVacio()
    {
        Responder(Pagina(0));

        var pantalla = await Pantalla();

        Assert.True(pantalla.SinEventos);
        Assert.Equal("0 eventos", pantalla.ResumenPagina);
    }

    [Fact]
    public async Task SinPermiso_MuestraElAvisoDeLaApiYNoElEstadoVacio()
    {
        _servicio.ConsultarAsync(Arg.Any<FiltroConsultaBitacora>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Tu rol no permite consultar la bitácora de auditoría.", new Dictionary<string, string>()));

        var pantalla = await Pantalla();

        Assert.Equal("Tu rol no permite consultar la bitácora de auditoría.", pantalla.Aviso?.Mensaje);
        Assert.True(pantalla.Aviso?.PuedeReintentar);
        Assert.False(pantalla.SinEventos);
        Assert.Empty(pantalla.Filas);
    }

    [Fact]
    public async Task SinConexion_EsMsg011()
    {
        _servicio.ConsultarAsync(Arg.Any<FiltroConsultaBitacora>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));

        var pantalla = await Pantalla();

        Assert.Equal("MSG-011", pantalla.Aviso?.Codigo);
        Assert.Equal("Sin conexión", pantalla.Aviso?.Titulo);
    }

    [Fact]
    public async Task EventoDeUsuarioSinNombre_SeMuestraComoNoDisponible()
    {
        Responder(Pagina(1, Evento(Laura, null, "LOGIN_OK")));

        var pantalla = await Pantalla();

        Assert.Equal(FilaBitacora.UsuarioSinNombre, pantalla.Filas.Single().Usuario);
    }

    [Fact]
    public void ElCatalogoTieneLasDoceAccionesDelValidador()
    {
        Assert.Equal(
            ["ACCESO_DENEGADO", "ACTUALIZAR", "CAMBIO_ROL", "CAMBIO_TENANT", "CIERRE_OT", "CIERRE_SESION", "CREAR", "ELIMINAR", "EXPORTAR", "IMPORTAR_CSV", "LOGIN_FALLIDO", "LOGIN_OK"],
            CatalogoAcciones.Todas.Select(a => a.Codigo).Order(StringComparer.Ordinal));
    }
}
