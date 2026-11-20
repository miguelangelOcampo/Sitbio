using Sitbio.Desktop.Controls;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public class PanelViewModelTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly IServicioTiquetes _tiquetes = Substitute.For<IServicioTiquetes>();
    private readonly IServicioMantenimiento _mantenimiento = Substitute.For<IServicioMantenimiento>();
    private readonly IServicioReportes _reportes = Substitute.For<IServicioReportes>();

    public PanelViewModelTests()
    {
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(EquiposPrueba.Pagina(0));
        _ordenes.ListarAsync(default!, default).ReturnsForAnyArgs(new PaginaOrdenes([], 1, 100, 0));
        _tiquetes.ListarAsync(default!, default).ReturnsForAnyArgs(new PaginaTiquetes([], 1, 100, 0));
        _mantenimiento.AlertasAsync(default).ReturnsForAnyArgs(new ResumenAlertas(30, [], 0));
        _reportes.IndicadoresAsync(default, default, default).ReturnsForAnyArgs(Kpi());
    }

    private static Indicadores Kpi() => new(
        new DateOnly(2026, 10, 7),
        Hoy,
        new CumplimientoPreventivo(10, 8, 80),
        5.5,
        300,
        new CostoPorEquipo(1_000_000m, 10, 100_000m),
        new FueraDeServicio(1, 10, 10),
        true);

    private static DateOnly Hoy => EquiposPrueba.Hoy;

    private static Orden Orden(string estado, string tipo = "correctivo", string prioridad = Criticidades.Media, DateTimeOffset? cierre = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "TQT-000001", Guid.NewGuid(), "Andrés", tipo, prioridad, estado, null,
            Guid.NewGuid(), "HSV-EQ-0001", "Bomba de infusión", "Urgencias",
            FechaAsignacion: Ahora.AddDays(-3), FechaCierre: cierre);

    private static Tiquete Tiquete(string codigo, string criticidad) =>
        new(Guid.NewGuid(), codigo, Guid.NewGuid(), "HSV-EQ-0002", "S-1", "Monitor", "UCI", "electrica", "No enciende",
            criticidad, criticidad, EstadosTiquete.Recibido, Ahora.AddHours(-2), null, Guid.NewGuid(), "Laura", "OPERATIVO", false);

    private static Alerta Alerta(string tipo, string estado, int dias) =>
        new(tipo, estado, Hoy.AddDays(dias), dias, Guid.NewGuid(), "HSV-EQ-0003", "Desfibrilador", "Urgencias", "Plan", Guid.NewGuid(), null, null);

    private void Ordenes(string estado, params Orden[] items) =>
        _ordenes.ListarAsync(Arg.Is<FiltroOrdenes>(f => f.Estado == estado), Arg.Any<CancellationToken>())
            .Returns(new PaginaOrdenes(items, 1, 100, items.Length));

    private PanelViewModel Crear(string rol, string nombres = "Laura María")
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", Ahora.AddMinutes(30), rol, false, nombres, "Gómez", "Hospital San Vicente"));
        return new PanelViewModel(_equipos, _ordenes, _tiquetes, _mantenimiento, _reportes, sesion, new EquiposPrueba.Reloj());
    }

    [Fact]
    public void Saluda_por_la_hora_con_el_primer_nombre_y_la_institucion()
    {
        var vm = Crear("AC-02");

        Assert.Equal("Buenos días, Laura", vm.Saludo);
        Assert.Equal("Así está el parque biomédico hoy, viernes 6 de noviembre de 2026.", vm.Subtitulo);
        Assert.Equal("Hospital San Vicente", vm.Institucion);
    }

    [Fact]
    public async Task Ingeniero_ve_los_cuatro_indicadores_con_el_inventario_y_las_ordenes()
    {
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(EquiposPrueba.Pagina(
            4,
            EquiposPrueba.Equipo("A", plan: EquiposPrueba.Plan(Hoy.AddDays(3))),
            EquiposPrueba.Equipo("B", plan: EquiposPrueba.Plan(Hoy.AddDays(-2), vencido: true)),
            EquiposPrueba.Equipo("C", estado: EstadosEquipo.FueraDeServicio),
            EquiposPrueba.Equipo("D", estado: EstadosEquipo.Baja)));
        Ordenes(EstadosOrden.Asignada, Orden(EstadosOrden.Asignada));
        Ordenes(EstadosOrden.Cerrada, Orden(EstadosOrden.Cerrada, cierre: Ahora.AddDays(-1)));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["Equipos registrados", "Equipos operativos", "Mantenimientos pendientes", "Órdenes abiertas"], vm.Indicadores.Select(i => i.Titulo));
        Assert.Equal("3", vm.Indicadores[0].Valor);
        Assert.Equal("2", vm.Indicadores[1].Valor);
        Assert.Equal("67 % disponibles", vm.Indicadores[1].Detalle);
        Assert.Equal("2", vm.Indicadores[2].Valor);
        Assert.Equal("1 vencidos · 1 esta semana", vm.Indicadores[2].Detalle);
        Assert.Equal("2", vm.Indicadores[3].Valor);
        Assert.Equal("1 por aprobar", vm.Indicadores[3].Detalle);
        Assert.True(vm.PuedeRegistrarEquipo);
        Assert.Null(vm.Aviso);
    }

    [Fact]
    public async Task Ingeniero_ve_alertas_ordenes_por_aprobar_y_tiquetes_sin_tecnico_con_su_destino()
    {
        var calibracion = Alerta(TiposAlerta.Calibracion, TiposAlerta.Vencida, -4);
        _mantenimiento.AlertasAsync(default).ReturnsForAnyArgs(new ResumenAlertas(30, [
            Alerta(TiposAlerta.Preventivo, TiposAlerta.Vencida, -1),
            Alerta(TiposAlerta.Preventivo, TiposAlerta.Proxima, 5),
            calibracion,
        ], 0));
        Ordenes(EstadosOrden.Cerrada, Orden(EstadosOrden.Cerrada, cierre: Ahora.AddDays(-1)));
        _tiquetes.ListarAsync(default!, default).ReturnsForAnyArgs(new PaginaTiquetes([Tiquete("TQT-000009", Criticidades.Critica)], 1, 100, 1));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(
            ["prev-vencidos", "prev-semana", $"cal-{calibracion.EquipoId}", "ot-aprobar", "tq-sin-tecnico"],
            vm.Atencion.Select(t => t.Clave));
        Assert.Equal("1 tiquete sin técnico asignado", vm.Atencion[^1].Titulo);
        Assert.Equal(BandejaViewModel.IdPantalla, vm.Atencion[^1].Destino);
        Assert.Equal($"{MantenimientosViewModel.PrefijoCalibracion}{calibracion.EquipoId}", vm.Atencion[2].Parametro);
        Assert.False(vm.SinAtencion);
    }

    [Fact]
    public async Task Ver_detalle_navega_y_descartar_quita_la_tarjeta_hasta_recargar()
    {
        Ordenes(EstadosOrden.Cerrada, Orden(EstadosOrden.Cerrada, cierre: Ahora.AddDays(-1)));
        var vm = Crear("AC-02");
        await vm.CargarCommand.ExecuteAsync(null);
        SolicitudNavegacion? solicitud = null;
        vm.NavegacionSolicitada += (_, s) => solicitud = s;

        vm.VerDetalleCommand.Execute(vm.Atencion[0]);
        vm.DescartarCommand.Execute(vm.Atencion[0]);
        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(OrdenesViewModel.IdPantalla, solicitud?.IdOpcion);
        Assert.Empty(vm.Atencion);
        Assert.True(vm.SinAtencion);
    }

    [Fact]
    public async Task Administrador_no_consulta_alertas_y_sus_tiquetes_van_al_seguimiento()
    {
        _tiquetes.ListarAsync(default!, default).ReturnsForAnyArgs(new PaginaTiquetes([Tiquete("TQT-000009", Criticidades.Media)], 1, 100, 1));
        var vm = Crear("AC-01");

        await vm.CargarCommand.ExecuteAsync(null);

        await _mantenimiento.DidNotReceiveWithAnyArgs().AlertasAsync(default);
        Assert.Equal(SeguimientoViewModel.IdPantalla, Assert.Single(vm.Atencion).Destino);
        Assert.False(vm.PuedeRegistrarEquipo);
        Assert.Equal("Órdenes abiertas", vm.Indicadores[^1].Titulo);
    }

    [Fact]
    public async Task Auditor_no_consulta_ordenes_y_ve_cumplimiento_e_indicadores_del_periodo()
    {
        var vm = Crear("AC-05");

        await vm.CargarCommand.ExecuteAsync(null);

        await _ordenes.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
        await _tiquetes.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
        Assert.Equal("Cumplimiento preventivo", vm.Indicadores[^1].Titulo);
        Assert.Equal("80 %", vm.Indicadores[^1].Valor);
        Assert.True(vm.MuestraIndicadoresPeriodo);
        Assert.Equal(4, vm.IndicadoresPeriodo.Count);
    }

    [Fact]
    public async Task Directiva_ve_el_panel_solo_con_indicadores_sin_consultar_el_inventario()
    {
        var vm = Crear("AC-06");

        await vm.CargarCommand.ExecuteAsync(null);

        await _equipos.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
        await _ordenes.DidNotReceiveWithAnyArgs().ListarAsync(default!, default);
        Assert.Equal(["Equipos activos", "Equipos operativos", "Tiempo medio de reparación", "Cumplimiento preventivo"], vm.Indicadores.Select(i => i.Titulo));
        Assert.Equal("9", vm.Indicadores[1].Valor);
        Assert.Equal("5,5 h", vm.Indicadores[2].Valor);
        Assert.True(vm.SinAtencion);
    }

    [Fact]
    public async Task Gestor_ve_inventario_y_ordenes_sin_alertas()
    {
        var vm = Crear("AC-07");

        await vm.CargarCommand.ExecuteAsync(null);

        await _mantenimiento.DidNotReceiveWithAnyArgs().AlertasAsync(default);
        Assert.Equal(4, vm.Indicadores.Count);
        Assert.False(vm.MuestraIndicadoresPeriodo);
    }

    [Fact]
    public async Task Actividad_agrupa_cierres_por_mes_semana_o_dia_y_lista_lo_reciente()
    {
        Ordenes(
            EstadosOrden.Aprobada,
            Orden(EstadosOrden.Aprobada, "preventivo", cierre: Ahora.AddDays(-1)),
            Orden(EstadosOrden.Aprobada, "correctivo", cierre: Ahora.AddDays(-20)));
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(EquiposPrueba.Pagina(0));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["jun", "jul", "ago", "sept", "oct", "nov"], vm.Serie.Select(p => p.Etiqueta));
        Assert.Equal(1, vm.Serie[^1].Preventivos);
        Assert.Equal(1, vm.Serie[^2].Correctivos);
        Assert.Equal(vm.Serie.Select(p => p.Etiqueta), vm.EtiquetasActividad);
        Assert.Equal(["Preventivos", "Correctivos"], vm.SeriesActividad.Select(x => x.Nombre));
        Assert.Equal(vm.Serie.Select(p => (double)p.Preventivos), vm.SeriesActividad[0].Valores);

        vm.CambiarPeriodoCommand.Execute(PanelViewModel.Periodo7Dias);
        Assert.Equal(8, vm.Serie.Count);
        Assert.Equal(1, vm.Serie.Sum(p => p.Preventivos));
        Assert.Equal(0, vm.Serie.Sum(p => p.Correctivos));

        vm.CambiarPeriodoCommand.Execute(PanelViewModel.Periodo30Dias);
        Assert.Equal(6, vm.Serie.Count);
        Assert.StartsWith("S", vm.Serie[0].Etiqueta);

        Assert.Equal("Mantenimiento preventivo aprobado", vm.Recientes[0].Titulo);
        Assert.Equal(2, vm.Recientes.Count);
    }

    [Fact]
    public async Task Un_error_parcial_muestra_aviso_y_completa_con_los_indicadores()
    {
        _ordenes.ListarAsync(default!, default).ThrowsAsyncForAnyArgs(new ErrorApi(null, "Sin red", new Dictionary<string, string>(), sinConexion: true));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-011", vm.Aviso?.Codigo);
        Assert.Equal("Cumplimiento preventivo", vm.Indicadores[^1].Titulo);
        Assert.False(vm.Cargando);
    }

    // ---------- I-12: íconos y mini gráficos de los indicadores ----------

    [Fact]
    public async Task Los_indicadores_traen_icono_y_mini_grafico_como_el_prototipo()
    {
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(EquiposPrueba.Pagina(
            4,
            EquiposPrueba.Equipo("A", plan: EquiposPrueba.Plan(Hoy.AddDays(3))),
            EquiposPrueba.Equipo("B", plan: EquiposPrueba.Plan(Hoy.AddDays(-2), vencido: true)),
            EquiposPrueba.Equipo("C", estado: EstadosEquipo.FueraDeServicio, plan: EquiposPrueba.Plan(Hoy.AddDays(20))),
            EquiposPrueba.Equipo("D", estado: EstadosEquipo.Baja)));
        Ordenes(EstadosOrden.Asignada, Orden(EstadosOrden.Asignada), Orden(EstadosOrden.Asignada) with { FechaAsignacion = Ahora.AddDays(-60) });
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["Icono.Equipo", "Icono.CheckCirculo", "Icono.Calendario", "Icono.Portapapeles"], vm.Indicadores.Select(i => i.Icono));
        Assert.Equal([TipoGrafico.Barras, TipoGrafico.Arco, TipoGrafico.Barras, TipoGrafico.Linea], vm.Indicadores.Select(i => i.Grafico));
        Assert.All(vm.Indicadores, i => Assert.True(i.HayGrafico));

        var tipos = vm.Indicadores[0];
        Assert.Equal(["Monitor de signos vitales"], tipos.Etiquetas);
        Assert.Equal([3.0], tipos.Series[0].Valores);

        var operativos = vm.Indicadores[1];
        Assert.True(operativos.EsArco);
        Assert.Equal([2.0, 1.0], operativos.Series.Select(s => s.Valores[0]));

        var pendientes = vm.Indicadores[2];
        Assert.Equal("Brush.Warning", pendientes.Series[0].ClaveColor);
        Assert.Equal([2.0, 0, 1, 0], pendientes.Series[0].Valores);

        var ordenes = vm.Indicadores[3];
        Assert.Equal(PanelViewModel.SemanasOrdenes, ordenes.Etiquetas.Count);
        Assert.Equal(1, ordenes.Series[0].Valores.Sum());
        Assert.Equal(1, ordenes.Series[0].Valores[^1]);
    }

    [Fact]
    public async Task Los_equipos_por_tipo_muestran_a_lo_sumo_seis_barras()
    {
        var equipos = Enumerable.Range(0, 8)
            .Select(i => EquiposPrueba.Equipo($"E{i}") with { TipoEquipo = $"Tipo {i}" })
            .ToArray();
        _equipos.ListarAsync(default!, default).ReturnsForAnyArgs(EquiposPrueba.Pagina(equipos.Length, equipos));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(PanelViewModel.MaximoBarras, vm.Indicadores[0].Etiquetas.Count);
    }

    [Fact]
    public async Task Sin_ordenes_ni_equipos_el_indicador_no_dibuja_un_grafico_vacio()
    {
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.All(vm.Indicadores, i => Assert.False(i.HayGrafico));
        Assert.All(vm.Indicadores, i => Assert.True(i.TieneIcono));
    }

    [Fact]
    public async Task La_directiva_ve_arcos_de_disponibilidad_y_cumplimiento()
    {
        var vm = Crear("AC-06");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(["Icono.Equipo", "Icono.CheckCirculo", "Icono.Reloj", "Icono.ListaCheck"], vm.Indicadores.Select(i => i.Icono));
        Assert.Equal([9.0, 1.0], vm.Indicadores[1].Series.Select(s => s.Valores[0]));
        Assert.Equal([8.0, 2.0], vm.Indicadores[3].Series.Select(s => s.Valores[0]));
        Assert.False(vm.Indicadores[0].HayGrafico);
        Assert.False(vm.Indicadores[2].HayGrafico);
    }

    // ---------- I-13: actividad reciente y atención requerida con íconos ----------

    [Fact]
    public async Task La_actividad_reciente_lleva_el_icono_del_tipo_de_evento_y_su_etiqueta()
    {
        Ordenes(
            EstadosOrden.Aprobada,
            Orden(EstadosOrden.Aprobada, "preventivo", cierre: Ahora.AddHours(-1)),
            Orden(EstadosOrden.Aprobada, "correctivo", cierre: Ahora.AddDays(-2)));
        Ordenes(EstadosOrden.Pausada, Orden(EstadosOrden.Pausada) with { FechaInicio = Ahora.AddHours(-5) });
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        var aprobada = vm.Recientes.First(r => r.Titulo == "Mantenimiento preventivo aprobado");
        Assert.Equal(("Icono.EscudoCheck", "Preventivo", true, TonoTarjeta.Exito), (aprobada.Icono, aprobada.Tipo, aprobada.EsPreventivo, aprobada.Tono));
        Assert.Contains(vm.Recientes, r => r.Icono == "Icono.Pausa" && r.Tono == TonoTarjeta.Advertencia && !r.EsPreventivo);
        Assert.All(vm.Recientes, r => Assert.False(string.IsNullOrEmpty(r.Cuando)));
    }

    [Fact]
    public async Task Atencion_requerida_dice_la_prioridad_y_cuenta_en_singular()
    {
        _mantenimiento.AlertasAsync(default).ReturnsForAnyArgs(new ResumenAlertas(30, [
            Alerta(TiposAlerta.Preventivo, TiposAlerta.Vencida, -1),
            Alerta(TiposAlerta.Preventivo, TiposAlerta.Vencida, -3),
            Alerta(TiposAlerta.Calibracion, TiposAlerta.Proxima, 9),
        ], 0));
        Ordenes(EstadosOrden.Cerrada, Orden(EstadosOrden.Cerrada, cierre: Ahora.AddDays(-1)));
        var vm = Crear("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(
            [("2 mantenimientos vencidos", "Prioridad crítica"), ("1 calibración por vencer", "Prioridad media"), ("1 orden por aprobar", "Prioridad media")],
            vm.Atencion.Select(t => (t.Titulo, t.TextoPrioridad)));
        Assert.Equal(["Icono.Calendario", "Icono.Regla", "Icono.Portapapeles"], vm.Atencion.Select(t => t.Icono));
    }
}
