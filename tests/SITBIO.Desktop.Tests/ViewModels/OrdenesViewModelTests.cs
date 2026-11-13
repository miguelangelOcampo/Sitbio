using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-012 «Mis órdenes» (HU-005): el técnico ve sus órdenes, las inicia, pausa y reanuda y abre la intervención; los demás roles solo consultan.</summary>
public class OrdenesViewModelTests
{
    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IServicioConfiguracionCampos _configuracion = Substitute.For<IServicioConfiguracionCampos>();
    private readonly List<FiltroOrdenes> _consultas = [];

    internal static Orden Orden(string estado, string codigo = "TQT-000007", string prioridad = Criticidades.Alta, bool intervencion = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), codigo, Guid.NewGuid(), "Carlos Ruiz", "correctivo", prioridad, estado, new DateOnly(2026, 11, 14),
            Guid.NewGuid(), "BOM-05", "Bomba de infusión", "UCI", "No enciende", null, null, intervencion);

    private async Task<OrdenesViewModel> AbrirAsync(string rol, params Orden[] ordenes)
    {
        _ordenes.ListarAsync(Arg.Any<FiltroOrdenes>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _consultas.Add(llamada.ArgAt<FiltroOrdenes>(0));
            return new PaginaOrdenes(ordenes, 1, OrdenesViewModel.TamanoPagina, ordenes.Length);
        });
        var vm = new OrdenesViewModel(_ordenes, _equipos, _configuracion, EquiposPrueba.Sesion(rol));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Tecnico_PideSusOrdenes_YCadaEstadoOfreceSusAcciones()
    {
        var vm = await AbrirAsync(
            "AC-03",
            Orden(EstadosOrden.Asignada, "TQT-000001", Criticidades.Critica),
            Orden(EstadosOrden.EnEjecucion, "TQT-000002"),
            Orden(EstadosOrden.Pausada, "TQT-000003", intervencion: true),
            Orden(EstadosOrden.Cerrada, "TQT-000004", intervencion: true));

        Assert.Equal([new FiltroOrdenes(null, true, 1, OrdenesViewModel.TamanoPagina)], _consultas);
        Assert.Equal(("Mis órdenes", "1–4 de 4 órdenes"), (vm.Titulo, vm.ResumenPagina));
        var (asignada, ejecucion, pausada, cerrada) = (vm.Filas[0], vm.Filas[1], vm.Filas[2], vm.Filas[3]);

        Assert.Equal(("Crítica", TonoEstado.Critico, "Asignada"), (asignada.Prioridad, asignada.TonoPrioridad, asignada.Estado));
        Assert.Equal((true, "Iniciar", false, false), (asignada.PuedeIniciar, asignada.TextoIniciar, asignada.PuedePausar, asignada.MuestraIntervencion));
        Assert.Equal((false, true, true, "Registrar intervención"), (ejecucion.PuedeIniciar, ejecucion.PuedePausar, ejecucion.Editable, ejecucion.TextoIntervencion));
        Assert.Equal((true, "Reanudar", true, "Editar intervención"), (pausada.PuedeIniciar, pausada.TextoIniciar, pausada.Editable, pausada.TextoIntervencion));
        Assert.Equal((false, false, false, true, "Ver intervención"), (cerrada.PuedeIniciar, cerrada.PuedePausar, cerrada.Editable, cerrada.MuestraIntervencion, cerrada.TextoIntervencion));
        Assert.Equal(("BOM-05 · Bomba de infusión", "Intervención registrada"), (cerrada.Equipo, cerrada.Intervencion));
    }

    [Fact]
    public async Task OtrosRoles_ConsultanTodasSinEjecutar()
    {
        var vm = await AbrirAsync("AC-02", Orden(EstadosOrden.EnEjecucion), Orden(EstadosOrden.Asignada, intervencion: false), Orden(EstadosOrden.Pausada, intervencion: true));

        Assert.Equal([new FiltroOrdenes(null, false, 1, OrdenesViewModel.TamanoPagina)], _consultas);
        Assert.Equal("Órdenes de trabajo", vm.Titulo);
        Assert.All(vm.Filas, f => Assert.False(f.PuedeIniciar || f.PuedePausar || f.Editable));
        Assert.Equal([false, false, true], vm.Filas.Select(f => f.MuestraIntervencion));

        await vm.IniciarCommand.ExecuteAsync(vm.Filas[1]);
        await _ordenes.DidNotReceiveWithAnyArgs().CambiarEstadoAsync(default, default!, default);
    }

    [Fact]
    public async Task Iniciar_YPausar_ActualizanLaFila_YConfirman()
    {
        var asignada = Orden(EstadosOrden.Asignada);
        var vm = await AbrirAsync("AC-03", asignada);
        _ordenes.CambiarEstadoAsync(asignada.Id, AccionesOrden.Iniciar, Arg.Any<CancellationToken>()).Returns(asignada with { Estado = EstadosOrden.EnEjecucion });
        _ordenes.CambiarEstadoAsync(asignada.Id, AccionesOrden.Pausar, Arg.Any<CancellationToken>()).Returns(asignada with { Estado = EstadosOrden.Pausada });

        await vm.IniciarCommand.ExecuteAsync(vm.Filas[0]);
        Assert.Equal(("En ejecución", true), (vm.Filas[0].Estado, vm.Filas[0].PuedePausar));
        Assert.Equal("Iniciaste la orden TQT-000007. Ya puedes registrar la intervención.", vm.Exito);

        await vm.PausarCommand.ExecuteAsync(vm.Filas[0]);
        Assert.Equal(("Pausada", "Reanudar"), (vm.Filas[0].Estado, vm.Filas[0].TextoIniciar));
        Assert.Equal("Pausaste la orden TQT-000007.", vm.Exito);
        Assert.Null(vm.Cambiando);
    }

    [Fact]
    public async Task CambioRechazado_MuestraElMensajeYElCodigo_SinCambiarLaFila()
    {
        var pausada = Orden(EstadosOrden.EnEjecucion);
        var vm = await AbrirAsync("AC-03", pausada);
        _ordenes.CambiarEstadoAsync(pausada.Id, AccionesOrden.Pausar, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("OT-002", "Solo puedes pausar una orden en ejecución.", new Dictionary<string, string>()));

        await vm.PausarCommand.ExecuteAsync(vm.Filas[0]);

        Assert.Equal(("No se pudo pausar la orden TQT-000007", "OT-002"), (vm.Aviso!.Titulo, vm.Aviso.Codigo));
        Assert.Equal("En ejecución", vm.Filas[0].Estado);
        Assert.Null(vm.Exito);
    }

    [Fact]
    public async Task FiltroPorEstado_ConsultaDeNuevoDesdeLaPrimeraPagina()
    {
        var vm = await AbrirAsync("AC-03", Orden(EstadosOrden.Pausada));

        vm.Estado = OrdenesViewModel.OpcionesEstado.Single(o => o.Valor == EstadosOrden.Pausada);

        Assert.Equal(new FiltroOrdenes(EstadosOrden.Pausada, true, 1, OrdenesViewModel.TamanoPagina), _consultas[^1]);
    }

    [Fact]
    public async Task SinOrdenes_LoExplica_YErrorDeConexionPermiteReintentar()
    {
        var vm = await AbrirAsync("AC-03");
        Assert.True(vm.SinOrdenes);
        Assert.Contains("No tienes órdenes asignadas", vm.TextoSinOrdenes);

        _ordenes.ListarAsync(Arg.Any<FiltroOrdenes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
        Assert.False(vm.SinOrdenes);
    }

    [Fact]
    public async Task AbrirIntervencion_ReemplazaLaLista_YAlGuardarVuelveConLaFilaMarcada()
    {
        var orden = Orden(EstadosOrden.EnEjecucion);
        var vm = await AbrirAsync("AC-03", orden);
        var equipo = EquiposPrueba.Equipo();
        _equipos.ObtenerAsync(orden.EquipoId, Arg.Any<CancellationToken>()).Returns(equipo);
        _configuracion.ObtenerAsync(equipo.TipoEquipoId, Arg.Any<CancellationToken>())
            .Returns(new ConfiguracionCampos(equipo.TipoEquipoId, "Monitor", null, null, [], []));
        _ordenes.ObtenerIntervencionAsync(orden.Id, Arg.Any<CancellationToken>()).Returns((Intervencion?)null);

        vm.AbrirIntervencionCommand.Execute(vm.Filas[0]);

        var intervencion = Assert.IsType<IntervencionViewModel>(vm.Intervencion);
        Assert.False(vm.EnLista);
        Assert.True(intervencion.Editable);
        intervencion.Diagnostico = "Sensor descalibrado";
        intervencion.AccionesRealizadas = "Calibración";
        await intervencion.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EnLista);
        Assert.Equal(("Intervención registrada", "Editar intervención"), (vm.Filas[0].Intervencion, vm.Filas[0].TextoIntervencion));
        Assert.Equal("Intervención guardada para la orden TQT-000007.", vm.Exito);
    }

    [Fact]
    public async Task AbrirIntervencion_SinIntervencionYSinPoderEditar_NoAbre()
    {
        var vm = await AbrirAsync("AC-03", Orden(EstadosOrden.Asignada));

        vm.AbrirIntervencionCommand.Execute(vm.Filas[0]);

        Assert.Null(vm.Intervencion);
    }

    [Fact]
    public void Fabrica_UI012_AbreLasOrdenesYLasCarga()
    {
        _ordenes.ListarAsync(Arg.Any<FiltroOrdenes>(), Arg.Any<CancellationToken>()).Returns(new PaginaOrdenes([Orden(EstadosOrden.Asignada)], 1, 20, 1));
        var servicios = new ServiceCollection()
            .AddSingleton(_ordenes)
            .AddSingleton(_equipos)
            .AddSingleton(_configuracion)
            .AddSingleton(EquiposPrueba.Sesion("AC-03"))
            .AddTransient<OrdenesViewModel>()
            .BuildServiceProvider();

        var pantalla = Assert.IsType<OrdenesViewModel>(new FabricaPantallas(servicios).Crear("UI-012"));

        Assert.True(pantalla.Cargado);
        Assert.Equal(["TQT-000007"], pantalla.Filas.Select(f => f.Referencia));
    }
}
