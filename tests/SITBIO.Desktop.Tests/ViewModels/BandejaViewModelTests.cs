using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-011 Bandeja de tiquetes y asignación (HU-004, HU-018): prioriza por criticidad, panel de técnicos con su carga y «Asignar y crear orden».</summary>
public class BandejaViewModelTests
{
    // 13/11/2026 10:00 en Colombia.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private readonly IServicioTiquetes _tiquetes = Substitute.For<IServicioTiquetes>();
    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly List<FiltroTiquetes> _consultas = [];
    private readonly TecnicoAsignable _carlos = new(Guid.NewGuid(), "Carlos Ruiz", 0);
    private readonly TecnicoAsignable _diana = new(Guid.NewGuid(), "Diana Torres", 2);

    private static Tiquete Tiquete(string codigo, string criticidad, string estado = EstadosTiquete.Recibido, bool fueraDeServicio = false) =>
        new(Guid.NewGuid(), codigo, Guid.NewGuid(), "HSV-EQ-0412", "MX-1", "Monitor", "UCI", "No enciende", "", criticidad, criticidad, estado,
            Ahora.AddHours(-2), null, Guid.NewGuid(), "Laura Gómez", fueraDeServicio ? "FUERA_DE_SERVICIO" : "OPERATIVO", fueraDeServicio);

    private static ErrorApi Error(string mensaje, string? codigo = null, Dictionary<string, string>? errores = null, bool sinConexion = false) =>
        new(codigo, mensaje, errores ?? [], sinConexion);

    private static TimeProvider Reloj()
    {
        var reloj = Substitute.For<TimeProvider>();
        reloj.GetUtcNow().Returns(Ahora);
        reloj.LocalTimeZone.Returns(TimeZoneInfo.CreateCustomTimeZone("Colombia", TimeSpan.FromHours(-5), "Colombia", "Colombia"));
        return reloj;
    }

    private async Task<BandejaViewModel> AbrirAsync(params Tiquete[] tiquetes)
    {
        var lista = tiquetes.Length > 0
            ? tiquetes
            : [Tiquete("TQT-000003", Criticidades.Critica, fueraDeServicio: true), Tiquete("TQT-000001", Criticidades.Media)];
        _tiquetes.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _consultas.Add(llamada.ArgAt<FiltroTiquetes>(0));
            return new PaginaTiquetes(lista, 1, BandejaViewModel.TamanoPagina, lista.Length);
        });
        _ordenes.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([_carlos, _diana]);
        var vm = new BandejaViewModel(_tiquetes, _ordenes, Reloj());
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private void AsignaA(TecnicoAsignable tecnico) =>
        _ordenes.AsignarAsync(Arg.Any<DatosOrden>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            var datos = llamada.ArgAt<DatosOrden>(0);
            return new Orden(Guid.NewGuid(), datos.TiqueteId, "TQT-000003", tecnico.Id, tecnico.Nombre, "correctivo", datos.Prioridad ?? "critica", "asignada", datos.FechaProgramada);
        });

    // ---------- Lista por criticidad ----------

    [Fact]
    public async Task Abrir_PideLosRecibidosPorCriticidad_YMuestraTextoNoSoloColor()
    {
        var vm = await AbrirAsync();

        Assert.Equal([new FiltroTiquetes(EstadosTiquete.Recibido, false, 1, BandejaViewModel.TamanoPagina, PorCriticidad: true)], _consultas);
        Assert.Equal(["TQT-000003", "TQT-000001"], vm.Filas.Select(f => f.Codigo));
        var critica = vm.Filas[0];
        Assert.Equal(("Crítica", TonoEstado.Critico, "Recibido"), (critica.Criticidad, critica.TonoCriticidad, critica.Estado));
        Assert.True(critica.EquipoFueraDeServicio);
        Assert.True(critica.PorAsignar);
        Assert.Equal("1–2 de 2 tiquetes por asignar", vm.ResumenPagina);
        Assert.False(vm.PanelAbierto);
    }

    [Fact]
    public async Task SinTiquetes_MuestraLaBandejaVacia()
    {
        _tiquetes.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(new PaginaTiquetes([], 1, 20, 0));
        _ordenes.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([_carlos]);
        var vm = new BandejaViewModel(_tiquetes, _ordenes, Reloj());

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinTiquetes);
        Assert.Equal("0 tiquetes por asignar", vm.ResumenPagina);
    }

    [Fact]
    public async Task ErrorAlConsultar_AvisaYPermiteReintentar()
    {
        _tiquetes.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).ThrowsAsync(Error("Sin conexión con el servidor.", sinConexion: true));
        _ordenes.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([_carlos]);
        var vm = new BandejaViewModel(_tiquetes, _ordenes, Reloj());

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
        Assert.False(vm.SinTiquetes);
    }

    // ---------- Panel de técnicos ----------

    [Fact]
    public async Task Panel_MuestraLosTecnicosActivosConSuCargaYLaPrioridadSugerida()
    {
        var vm = await AbrirAsync();

        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);

        Assert.True(vm.PanelAbierto);
        Assert.Equal("TQT-000003", vm.Seleccionado!.Codigo);
        Assert.Equal([("Carlos Ruiz", "Sin órdenes abiertas"), ("Diana Torres", "2 órdenes abiertas")], vm.Tecnicos.Select(t => (t.Nombre, t.Carga)));
        Assert.Null(vm.TecnicoElegido);
        Assert.Equal(Criticidades.Critica, vm.Prioridad.Valor);
        Assert.Equal("1 orden abierta", new FilaTecnico(Guid.NewGuid(), "X", 1).Carga);
    }

    [Fact]
    public async Task SinTecnicosActivos_LoExplicaConRn003()
    {
        _tiquetes.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(new PaginaTiquetes([Tiquete("TQT-000001", Criticidades.Alta)], 1, 20, 1));
        _ordenes.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([]);
        var vm = new BandejaViewModel(_tiquetes, _ordenes, Reloj());

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinTecnicos);
        Assert.Contains("RN-003", BandejaViewModel.SinTecnicosActivos);
    }

    // ---------- Asignar y crear orden (CA-HU-004-01) ----------

    [Fact]
    public async Task Asignar_CreaLaOrden_CierraElPanel_YLaFilaPasaAAsignado()
    {
        var vm = await AbrirAsync();
        AsignaA(_diana);
        var fila = vm.Filas[0];
        vm.AbrirAsignacionCommand.Execute(fila);
        vm.TecnicoElegido = vm.Tecnicos[1];
        vm.Prioridad = BandejaViewModel.OpcionesPrioridad[1];
        vm.FechaProgramada = "14/11/2026";

        await vm.AsignarCommand.ExecuteAsync(null);

        await _ordenes.Received(1).AsignarAsync(
            new DatosOrden(fila.Tiquete.Id, _diana.Id, Criticidades.Alta, new DateOnly(2026, 11, 14)), Arg.Any<CancellationToken>());
        Assert.False(vm.PanelAbierto);
        var asignada = vm.Filas[0];
        Assert.Equal(("Asignado a Diana Torres", TonoEstado.Advertencia, false), (asignada.Estado, asignada.Tono, asignada.PorAsignar));
        Assert.Contains("TQT-000003", vm.Exito);
        Assert.Contains("Diana Torres", vm.Exito);

        // La carga del técnico sube y el panel vuelve a ordenarse del menos al más ocupado.
        Assert.Equal([("Carlos Ruiz", 0), ("Diana Torres", 3)], vm.Tecnicos.Select(t => (t.Nombre, t.OrdenesAbiertas)));
    }

    [Fact]
    public async Task Asignar_SinFecha_NoLaEnviaYUsaLaPrioridadSugerida()
    {
        var vm = await AbrirAsync();
        AsignaA(_carlos);
        vm.AbrirAsignacionCommand.Execute(vm.Filas[1]);
        vm.TecnicoElegido = vm.Tecnicos[0];

        await vm.AsignarCommand.ExecuteAsync(null);

        await _ordenes.Received(1).AsignarAsync(new DatosOrden(vm.Filas[1].Tiquete.Id, _carlos.Id, Criticidades.Media, null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "14/11/2026", BandejaViewModel.TecnicoObligatorio, null)]
    [InlineData("Carlos", "2026-11-14", null, BandejaViewModel.FechaInvalida)]
    [InlineData("Carlos", "12/11/2026", null, BandejaViewModel.FechaPasada)]
    public async Task Asignar_ConDatosIncompletos_ExplicaJuntoAlCampoYNoLlamaALaApi(string tecnico, string fecha, string? errorTecnico, string? errorFecha)
    {
        var vm = await AbrirAsync();
        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);
        vm.TecnicoElegido = tecnico.Length > 0 ? vm.Tecnicos[0] : null;
        vm.FechaProgramada = fecha;

        await vm.AsignarCommand.ExecuteAsync(null);

        Assert.Equal((errorTecnico, errorFecha), (vm.ErrorTecnico, vm.ErrorFecha));
        Assert.True(vm.PanelAbierto);
        await _ordenes.DidNotReceiveWithAnyArgs().AsignarAsync(default!, default);
    }

    [Fact]
    public async Task Asignar_HoyEnColombia_SePermite()
    {
        var vm = await AbrirAsync();
        AsignaA(_carlos);
        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);
        vm.TecnicoElegido = vm.Tecnicos[0];
        vm.FechaProgramada = "13/11/2026";

        await vm.AsignarCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorFecha);
        Assert.False(vm.PanelAbierto);
    }

    // ---------- Rechazos de la API ----------

    [Fact]
    public async Task Asignar_RechazoRn003DeLaApi_SeMuestraJuntoAlTecnico()
    {
        var vm = await AbrirAsync();
        const string Rn003 = "Solo puedes asignar la orden a un Técnico de Mantenimiento activo de tu institución (RN-003).";
        _ordenes.AsignarAsync(Arg.Any<DatosOrden>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error(Rn003, errores: new Dictionary<string, string> { ["tecnicoId"] = Rn003 }));
        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);
        vm.TecnicoElegido = vm.Tecnicos[0];

        await vm.AsignarCommand.ExecuteAsync(null);

        Assert.Equal(Rn003, vm.ErrorTecnico);
        Assert.Null(vm.AvisoPanel);
        Assert.True(vm.PanelAbierto);
        Assert.True(vm.Filas[0].PorAsignar);
    }

    [Fact]
    public async Task Asignar_OtroIngenieroLoAsignoPrimero_AvisaYLaFilaYaNoSeAsigna()
    {
        var vm = await AbrirAsync();
        _ordenes.AsignarAsync(Arg.Any<DatosOrden>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error("El tiquete TQT-000003 ya tiene una orden de trabajo.", "OT-001"));
        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);
        vm.TecnicoElegido = vm.Tecnicos[0];

        await vm.AsignarCommand.ExecuteAsync(null);

        Assert.Equal(("OT-001", "El tiquete TQT-000003 ya tiene una orden de trabajo."), (vm.AvisoPanel!.Codigo, vm.AvisoPanel.Mensaje));
        Assert.Equal(("Asignado", false), (vm.Filas[0].Estado, vm.Filas[0].PorAsignar));
    }

    [Fact]
    public async Task FilaYaAsignada_NoAbreElPanel()
    {
        var vm = await AbrirAsync(Tiquete("TQT-000009", Criticidades.Alta, EstadosTiquete.Asignado));

        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);

        Assert.False(vm.PanelAbierto);
    }

    [Fact]
    public async Task Cancelar_CierraElPanelSinAsignar()
    {
        var vm = await AbrirAsync();
        vm.AbrirAsignacionCommand.Execute(vm.Filas[0]);

        vm.CerrarPanelCommand.Execute(null);

        Assert.False(vm.PanelAbierto);
        Assert.False(vm.AsignarCommand.CanExecute(null));
        await _ordenes.DidNotReceiveWithAnyArgs().AsignarAsync(default!, default);
    }

    // ---------- Navegación ----------

    [Fact]
    public void Fabrica_UI011_AbreLaBandejaYLaCarga()
    {
        _tiquetes.ListarAsync(Arg.Any<FiltroTiquetes>(), Arg.Any<CancellationToken>()).Returns(new PaginaTiquetes([Tiquete("TQT-000001", Criticidades.Alta)], 1, 20, 1));
        _ordenes.TecnicosAsync(Arg.Any<CancellationToken>()).Returns([_carlos]);
        var servicios = new ServiceCollection()
            .AddSingleton(_tiquetes)
            .AddSingleton(_ordenes)
            .AddSingleton(Reloj())
            .AddTransient<BandejaViewModel>()
            .BuildServiceProvider();

        var pantalla = Assert.IsType<BandejaViewModel>(new FabricaPantallas(servicios).Crear("UI-011"));

        Assert.True(pantalla.Cargado);
        Assert.Equal(["TQT-000001"], pantalla.Filas.Select(f => f.Codigo));
        Assert.Equal(["Carlos Ruiz"], pantalla.Tecnicos.Select(t => t.Nombre));
    }
}
