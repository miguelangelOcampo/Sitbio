using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-003 Panel de inicio (prototipo <c>screens/Panel.tsx</c>): saludo, cuatro indicadores según el rol, «Atención
/// requerida» (alertas de HU-020, órdenes por aprobar o de prioridad alta y tiquetes sin técnico), «Actividad de
/// mantenimiento» por periodo, actividad reciente y equipos con actividad. Solo usa las API que el rol puede consultar:
/// lo que el rol no ve no se pide ni se muestra (la API lo exige igual).
/// </summary>
public partial class PanelViewModel(
    IServicioEquipos equipos,
    IServicioOrdenes ordenes,
    IServicioTiquetes tiquetes,
    IServicioMantenimiento mantenimiento,
    IServicioReportes reportes,
    SesionActual sesion,
    TimeProvider tiempo) : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "UI-003";

    /// <summary>Área del gráfico de actividad, en unidades de la vista.</summary>
    public const double AnchoGrafico = 560;

    public const double AltoGrafico = 150;

    public const string Periodo7Dias = "7d";
    public const string Periodo30Dias = "30d";
    public const string Periodo6Meses = "6m";

    private const string Preventivo = "preventivo";
    private const int TamanoPagina = 100;
    private const int PaginasMaximas = 20;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private readonly List<Orden> _ordenesLeidas = [];
    private readonly HashSet<string> _descartadas = [];

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Serie), nameof(LineaPreventivos), nameof(LineaCorrectivos), nameof(MaximoSerie))]
    private string _periodo = Periodo6Meses;

    public ObservableCollection<TarjetaIndicador> Indicadores { get; } = [];

    public ObservableCollection<TarjetaAtencion> Atencion { get; } = [];

    public ObservableCollection<ActividadReciente> Recientes { get; } = [];

    public ObservableCollection<EquipoReciente> EquiposRecientes { get; } = [];

    public ObservableCollection<TarjetaIndicador> IndicadoresPeriodo { get; } = [];

    public static IReadOnlyList<OpcionPeriodo> Periodos { get; } =
    [
        new(Periodo7Dias, "7 días"),
        new(Periodo30Dias, "30 días"),
        new(Periodo6Meses, "6 meses"),
    ];

    private string Rol => sesion.Sesion?.Rol ?? string.Empty;

    /// <summary>GET /equipos: todos los roles del panel menos la Directiva (AC-06).</summary>
    public bool VeEquipos => Rol is "AC-01" or "AC-02" or "AC-05" or "AC-07";

    /// <summary>GET /ordenes y GET /tiquetes: AC-01, AC-02 y AC-07.</summary>
    public bool VeOrdenes => Rol is "AC-01" or "AC-02" or "AC-07";

    /// <summary>GET /alertas (HU-020): solo el Ingeniero (AC-02) entre los roles del panel.</summary>
    public bool VeAlertas => Rol == "AC-02";

    /// <summary>GET /indicadores (HU-026).</summary>
    public bool VeIndicadores => Rol is "AC-01" or "AC-02" or "AC-05" or "AC-06" or "AC-07";

    public bool PuedeRegistrarEquipo => Rol == "AC-02";

    public bool PuedeVerReportes => VeIndicadores;

    /// <summary>Sin órdenes (AC-05, AC-06) el gráfico se reemplaza por los indicadores del periodo.</summary>
    public bool MuestraIndicadoresPeriodo => !VeOrdenes && IndicadoresPeriodo.Count > 0;

    public bool SinAtencion => !Cargando && Atencion.Count == 0;

    public string Saludo
    {
        get
        {
            var hora = tiempo.GetLocalNow().Hour;
            var saludo = hora < 12 ? "Buenos días" : hora < 19 ? "Buenas tardes" : "Buenas noches";
            var nombre = sesion.Sesion?.Nombres.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrEmpty(nombre) ? saludo : $"{saludo}, {nombre}";
        }
    }

    public string Subtitulo
    {
        get
        {
            var fecha = tiempo.GetLocalNow().ToString("dddd d 'de' MMMM 'de' yyyy", Cultura);
            return $"Así está el parque biomédico hoy, {fecha}.";
        }
    }

    public string Institucion => sesion.Sesion?.Institucion ?? string.Empty;

    /// <summary>Barras del periodo: preventivos y correctivos cerrados por día, semana o mes.</summary>
    public IReadOnlyList<PuntoSerie> Serie => CalcularSerie();

    public int MaximoSerie => Math.Max(1, Serie.Count == 0 ? 0 : Serie.Max(p => Math.Max(p.Preventivos, p.Correctivos)));

    /// <summary>Puntos de la línea de preventivos para un Polyline ("x,y x,y ...").</summary>
    public string LineaPreventivos => Linea(p => p.Preventivos);

    public string LineaCorrectivos => Linea(p => p.Correctivos);

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        Aviso = null;
        Indicadores.Clear();
        Atencion.Clear();
        Recientes.Clear();
        EquiposRecientes.Clear();
        IndicadoresPeriodo.Clear();
        _ordenesLeidas.Clear();
        try
        {
            var hoy = DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);
            var listaEquipos = VeEquipos ? await IntentarAsync(() => TodosLosEquiposAsync()) : null;
            var abiertas = VeOrdenes ? await IntentarAsync(() => OrdenesAbiertasAsync()) : null;
            var sinTecnico = VeOrdenes ? await IntentarAsync(() => TiquetesSinTecnicoAsync()) : null;
            var alertas = VeAlertas ? await IntentarAsync(() => mantenimiento.AlertasAsync(CancellationToken.None)) : null;
            var indicadores = VeIndicadores ? await IntentarAsync(() => reportes.IndicadoresAsync(null, null, CancellationToken.None)) : null;
            if (VeOrdenes && await IntentarAsync(() => OrdenesAsync(EstadosOrden.Aprobada)) is { } aprobadas)
            {
                _ordenesLeidas.AddRange(aprobadas);
            }

            if (abiertas is not null)
            {
                _ordenesLeidas.AddRange(abiertas);
            }

            ArmarIndicadores(hoy, listaEquipos, abiertas, indicadores);
            ArmarAtencion(hoy, listaEquipos, abiertas, sinTecnico, alertas);
            ArmarActividad(listaEquipos);
            ArmarIndicadoresPeriodo(indicadores);
        }
        finally
        {
            Cargando = false;
            OnPropertyChanged(nameof(Serie));
            OnPropertyChanged(nameof(LineaPreventivos));
            OnPropertyChanged(nameof(LineaCorrectivos));
            OnPropertyChanged(nameof(MaximoSerie));
            OnPropertyChanged(nameof(SinAtencion));
            OnPropertyChanged(nameof(MuestraIndicadoresPeriodo));
        }
    }

    [RelayCommand]
    private void CambiarPeriodo(string? periodo)
    {
        if (periodo is Periodo7Dias or Periodo30Dias or Periodo6Meses)
        {
            Periodo = periodo;
        }
    }

    [RelayCommand]
    private void VerDetalle(TarjetaAtencion? tarjeta)
    {
        if (tarjeta is not null)
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(tarjeta.Destino, tarjeta.Parametro));
        }
    }

    [RelayCommand]
    private void Descartar(TarjetaAtencion? tarjeta)
    {
        if (tarjeta is not null && Atencion.Remove(tarjeta))
        {
            _descartadas.Add(tarjeta.Clave);
            OnPropertyChanged(nameof(SinAtencion));
        }
    }

    /// <summary>«Ver reportes», «Registrar equipo», «Ver todos los equipos» y los indicadores con destino.</summary>
    [RelayCommand]
    private void Ir(string? destino)
    {
        if (!string.IsNullOrEmpty(destino))
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(destino));
        }
    }

    // ---------------- Indicadores ----------------

    private void ArmarIndicadores(DateOnly hoy, IReadOnlyList<Equipo>? lista, IReadOnlyList<Orden>? abiertas, Indicadores? kpi)
    {
        if (lista is not null)
        {
            var activos = lista.Where(e => e.Estado != EstadosEquipo.Baja).ToList();
            var operativos = activos.Count(e => e.Estado == EstadosEquipo.Operativo);
            var tipos = activos.Select(e => e.TipoEquipo).Distinct().Count();
            var areas = activos.Select(e => e.Ubicacion).Distinct().Count();
            Indicadores.Add(new("Equipos registrados", Numero(activos.Count), $"{tipos} tipos · {areas} áreas", TonoTarjeta.Neutro, EquiposViewModel.IdPantalla));
            Indicadores.Add(new(
                "Equipos operativos",
                Numero(operativos),
                activos.Count == 0 ? "Sin equipos activos" : $"{Math.Round(operativos * 100.0 / activos.Count)} % disponibles",
                TonoTarjeta.Exito,
                null));

            var conPlan = activos.Where(e => e.Preventivo is not null).Select(e => e.Preventivo!).ToList();
            var vencidos = conPlan.Count(p => p.Vencido || p.ProximaFecha < hoy);
            var semana = conPlan.Count(p => p.ProximaFecha >= hoy && p.ProximaFecha <= hoy.AddDays(7));
            var pendientes = conPlan.Count(p => p.ProximaFecha <= hoy.AddDays(30));
            var detalle = vencidos > 0 ? $"{vencidos} vencidos · {semana} esta semana" : $"{semana} vencen esta semana";
            Indicadores.Add(new("Mantenimientos pendientes", Numero(pendientes), detalle, vencidos + semana > 0 ? TonoTarjeta.Advertencia : TonoTarjeta.Neutro, MantenimientosViewModel.IdPantalla));
        }
        else if (kpi is not null)
        {
            // La Directiva (AC-06) no consulta el inventario: el panel resume los indicadores de HU-026.
            var f = kpi.FueraDeServicio;
            Indicadores.Add(new("Equipos activos", Numero(f.EquiposActivos), "Inventario de la institución", TonoTarjeta.Neutro, null));
            Indicadores.Add(new(
                "Equipos operativos",
                Numero(f.EquiposActivos - f.Equipos),
                f.EquiposActivos == 0 ? "Sin equipos activos" : $"{Math.Round((f.EquiposActivos - f.Equipos) * 100.0 / f.EquiposActivos)} % disponibles",
                TonoTarjeta.Exito,
                null));
            Indicadores.Add(new("Tiempo medio de reparación", Horas(kpi.MttrHoras), "MTTR del periodo", TonoTarjeta.Neutro, ReportesViewModel.IdPantalla));
        }

        if (abiertas is not null)
        {
            var porAprobar = abiertas.Count(o => o.Estado == EstadosOrden.Cerrada);
            Indicadores.Add(new("Órdenes abiertas", Numero(abiertas.Count), $"{porAprobar} por aprobar", porAprobar > 0 ? TonoTarjeta.Info : TonoTarjeta.Neutro, OrdenesViewModel.IdPantalla));
        }
        else if (kpi is not null)
        {
            var c = kpi.CumplimientoPreventivo;
            Indicadores.Add(new(
                "Cumplimiento preventivo",
                c.Porcentaje is { } p ? $"{Math.Round(p)} %" : "—",
                c.Programadas == 0 ? "Sin preventivas programadas" : $"{c.Cumplidas} de {c.Programadas} preventivas",
                TonoTarjeta.Neutro,
                ReportesViewModel.IdPantalla));
        }
    }

    private void ArmarIndicadoresPeriodo(Indicadores? kpi)
    {
        if (VeOrdenes || kpi is null)
        {
            return;
        }

        IndicadoresPeriodo.Add(new("Tiempo medio de reparación", Horas(kpi.MttrHoras), "MTTR", TonoTarjeta.Neutro, null));
        IndicadoresPeriodo.Add(new("Tiempo medio entre fallas", Horas(kpi.MtbfHoras), "MTBF", TonoTarjeta.Neutro, null));
        IndicadoresPeriodo.Add(new(
            "Costo por equipo",
            kpi.CostoPorEquipo.Promedio is { } costo ? costo.ToString("C0", Cultura) : "—",
            "Promedio del periodo",
            TonoTarjeta.Neutro,
            null));
        IndicadoresPeriodo.Add(new(
            "Fuera de servicio",
            Numero(kpi.FueraDeServicio.Equipos),
            kpi.FueraDeServicio.Porcentaje is { } p ? $"{p.ToString("0.#", Cultura)} % del inventario" : "Al corte de hoy",
            kpi.FueraDeServicio.Equipos > 0 ? TonoTarjeta.Critico : TonoTarjeta.Neutro,
            null));
    }

    // ---------------- Atención requerida ----------------

    private void ArmarAtencion(DateOnly hoy, IReadOnlyList<Equipo>? lista, IReadOnlyList<Orden>? abiertas, IReadOnlyList<Tiquete>? sinTecnico, ResumenAlertas? alertas)
    {
        var tarjetas = new List<TarjetaAtencion>();
        if (alertas is not null)
        {
            var preventivas = alertas.Alertas.Where(a => a.Tipo == TiposAlerta.Preventivo).ToList();
            var vencidas = preventivas.Where(a => a.Estado == TiposAlerta.Vencida).ToList();
            if (vencidas.Count > 0)
            {
                tarjetas.Add(new(
                    "prev-vencidos", "Mantenimiento", "Crítica", TonoTarjeta.Critico,
                    $"{vencidas.Count} mantenimientos vencidos",
                    Equipos(vencidas.Select(a => $"{a.TipoEquipo} {a.PlacaInventario}")),
                    Hace(vencidas.Min(a => a.Fecha), hoy),
                    MantenimientosViewModel.IdPantalla,
                    null));
            }

            var semana = preventivas.Where(a => a.Estado != TiposAlerta.Vencida && a.DiasRestantes <= 7).ToList();
            if (semana.Count > 0)
            {
                tarjetas.Add(new(
                    "prev-semana", "Mantenimiento", "Media", TonoTarjeta.Advertencia,
                    $"{semana.Count} mantenimientos próximos a vencer",
                    Equipos(semana.Select(a => $"{a.TipoEquipo} {a.PlacaInventario}")),
                    "Esta semana",
                    MantenimientosViewModel.IdPantalla,
                    null));
            }

            foreach (var a in alertas.Alertas.Where(a => a.Tipo == TiposAlerta.Calibracion && a.Estado == TiposAlerta.Vencida).Take(2))
            {
                tarjetas.Add(new(
                    $"cal-{a.EquipoId}", "Calibración", "Crítica", TonoTarjeta.Critico,
                    $"Calibración vencida · {a.PlacaInventario}",
                    $"{a.TipoEquipo} · venció el {a.Fecha.ToString("dd/MM/yyyy", Cultura)}",
                    Hace(a.Fecha, hoy),
                    MantenimientosViewModel.IdPantalla,
                    $"{MantenimientosViewModel.PrefijoCalibracion}{a.EquipoId}"));
            }

            var porVencer = alertas.Alertas.Count(a => a.Tipo == TiposAlerta.Calibracion && a.Estado != TiposAlerta.Vencida);
            if (porVencer > 0)
            {
                tarjetas.Add(new(
                    "cal-proximas", "Calibración", "Media", TonoTarjeta.Advertencia,
                    $"{porVencer} calibraciones por vencer",
                    $"En los próximos {alertas.DiasAnticipacion} días",
                    "Próximamente",
                    MantenimientosViewModel.IdPantalla,
                    null));
            }
        }

        if (abiertas is not null)
        {
            var porAprobar = abiertas.Where(o => o.Estado == EstadosOrden.Cerrada).ToList();
            if (porAprobar.Count > 0)
            {
                tarjetas.Add(new(
                    "ot-aprobar", "Orden de trabajo", "Media", TonoTarjeta.Info,
                    $"{porAprobar.Count} órdenes por aprobar",
                    Equipos(porAprobar.Select(o => $"{o.TipoEquipo} {o.PlacaInventario}")),
                    Hace(porAprobar.Min(o => o.FechaCierre), hoy),
                    OrdenesViewModel.IdPantalla,
                    null));
            }

            var altas = abiertas.Where(o => o.Estado != EstadosOrden.Cerrada && o.Prioridad is Criticidades.Alta or Criticidades.Critica).ToList();
            if (altas.Count > 0)
            {
                tarjetas.Add(new(
                    "ot-alta", "Orden de trabajo", "Alta", TonoTarjeta.Alto,
                    $"{altas.Count} órdenes con prioridad alta",
                    Equipos(altas.Select(o => $"{o.TipoEquipo} {o.PlacaInventario}")),
                    "Hoy",
                    OrdenesViewModel.IdPantalla,
                    null));
            }
        }

        if (sinTecnico is { Count: > 0 })
        {
            var criticos = sinTecnico.Count(t => t.Criticidad == Criticidades.Critica);
            tarjetas.Add(new(
                "tq-sin-tecnico", "Solicitud", "Alta", TonoTarjeta.Critico,
                $"{sinTecnico.Count} tiquetes sin técnico asignado",
                criticos > 0 ? $"{criticos} de criticidad crítica" : string.Join(" · ", sinTecnico.Take(3).Select(t => t.Codigo)),
                Relativo(sinTecnico.Min(t => t.FechaReporte)),
                Rol == "AC-02" ? BandejaViewModel.IdPantalla : SeguimientoViewModel.IdPantalla,
                null));
        }

        if (lista is not null)
        {
            foreach (var e in lista.Where(e => e.Estado == EstadosEquipo.FueraDeServicio).Take(2))
            {
                tarjetas.Add(new(
                    $"fuera-{e.Id}", "Equipo", "Crítica", TonoTarjeta.Critico,
                    $"{e.TipoEquipo} fuera de servicio",
                    $"{e.PlacaInventario} · {e.Ubicacion}",
                    "Pendiente de revisión",
                    EquiposViewModel.IdPantalla,
                    null));
            }
        }

        foreach (var t in tarjetas.Where(t => !_descartadas.Contains(t.Clave)))
        {
            Atencion.Add(t);
        }
    }

    // ---------------- Actividad ----------------

    private void ArmarActividad(IReadOnlyList<Equipo>? lista)
    {
        var porMomento = _ordenesLeidas
            .Select(o => (Orden: o, Momento: UltimoMovimiento(o)))
            .Where(x => x.Momento is not null)
            .OrderByDescending(x => x.Momento)
            .ToList();

        foreach (var (o, momento) in porMomento.Take(4))
        {
            var preventiva = o.TipoMantenimiento == Preventivo;
            var (titulo, tono) = o.Estado switch
            {
                EstadosOrden.Aprobada => (preventiva ? "Mantenimiento preventivo aprobado" : "Mantenimiento correctivo aprobado", TonoTarjeta.Exito),
                EstadosOrden.Cerrada => ("Intervención técnica finalizada", TonoTarjeta.Exito),
                EstadosOrden.EnEjecucion => ("Orden de trabajo en ejecución", TonoTarjeta.Info),
                EstadosOrden.Pausada => ("Orden de trabajo pausada", TonoTarjeta.Advertencia),
                _ => ("Orden de trabajo asignada", TonoTarjeta.Info),
            };
            var detalle = $"{o.TipoEquipo} {o.PlacaInventario}{(o.CodigoTiquete is { } codigo ? $" · {codigo}" : string.Empty)}";
            Recientes.Add(new(titulo, detalle, preventiva ? "Preventivo" : "Correctivo", Relativo(momento!.Value), tono));
        }

        if (lista is null)
        {
            return;
        }

        var porId = lista.ToDictionary(e => e.Id);
        foreach (var id in porMomento.Select(x => x.Orden.EquipoId).Distinct().Take(6))
        {
            if (porId.TryGetValue(id, out var e))
            {
                var hoy = DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);
                var proximo = e.Preventivo?.ProximaFecha;
                var dias = proximo is { } p ? p.DayNumber - hoy.DayNumber : (int?)null;
                EquiposRecientes.Add(new(
                    e.Id,
                    e.PlacaInventario,
                    e.TipoEquipo,
                    $"{e.Marca} {e.Modelo}",
                    e.Ubicacion,
                    e.EstadoNombre,
                    TextosEquipo.Tono(e.Estado),
                    TextosEquipo.Fecha(proximo),
                    dias is >= 0 and <= 7 ? $"en {dias} d" : null));
            }
        }
    }

    private List<PuntoSerie> CalcularSerie()
    {
        var ahora = tiempo.GetLocalNow();
        var hoy = DateOnly.FromDateTime(ahora.DateTime);
        var cierres = _ordenesLeidas
            .Where(o => o.FechaCierre is not null)
            .Select(o => (Dia: DateOnly.FromDateTime(o.FechaCierre!.Value.ToOffset(ahora.Offset).DateTime), Preventiva: o.TipoMantenimiento == Preventivo))
            .ToList();

        List<(string Etiqueta, DateOnly Desde, DateOnly Hasta)> cubetas = Periodo switch
        {
            Periodo7Dias => Enumerable.Range(0, 8).Select(i => hoy.AddDays(i - 7)).Select(d => (d.ToString("ddd d", Cultura), d, d)).ToList(),
            Periodo30Dias => Enumerable.Range(0, 6).Select(i =>
            {
                var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7)).AddDays((i - 5) * 7);
                return ($"S{ISOWeek.GetWeekOfYear(lunes.ToDateTime(TimeOnly.MinValue))}", lunes, lunes.AddDays(6));
            }).ToList(),
            _ => Enumerable.Range(0, 6).Select(i =>
            {
                var mes = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(i - 5);
                return (mes.ToString("MMM", Cultura).TrimEnd('.'), mes, mes.AddMonths(1).AddDays(-1));
            }).ToList(),
        };

        return cubetas
            .Select(c => new PuntoSerie(
                c.Etiqueta,
                cierres.Count(x => x.Preventiva && x.Dia >= c.Desde && x.Dia <= c.Hasta),
                cierres.Count(x => !x.Preventiva && x.Dia >= c.Desde && x.Dia <= c.Hasta)))
            .ToList();
    }

    private string Linea(Func<PuntoSerie, int> valor)
    {
        var serie = Serie;
        if (serie.Count < 2)
        {
            return string.Empty;
        }

        // Cada punto queda al centro de su columna, debajo de la etiqueta del periodo.
        var maximo = MaximoSerie;
        var paso = AnchoGrafico / serie.Count;
        return string.Join(' ', serie.Select((p, i) => string.Create(
            CultureInfo.InvariantCulture,
            $"{(i + 0.5) * paso:0.#},{AltoGrafico - (valor(p) * AltoGrafico / maximo):0.#}")));
    }

    // ---------------- Lecturas ----------------

    private async Task<T?> IntentarAsync<T>(Func<Task<T>> leer)
        where T : class
    {
        try
        {
            return await leer();
        }
        catch (ErrorApi error)
        {
            Aviso ??= new Aviso(
                error.SinConexion ? "Sin conexión" : "Parte del panel no se pudo cargar",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
            return null;
        }
    }

    private async Task<IReadOnlyList<Equipo>> TodosLosEquiposAsync()
    {
        var todos = new List<Equipo>();
        for (var pagina = 1; pagina <= PaginasMaximas; pagina++)
        {
            var leida = await equipos.ListarAsync(new FiltroEquipos(Pagina: pagina, Tamano: TamanoPagina), CancellationToken.None);
            todos.AddRange(leida.Items);
            if (todos.Count >= leida.Total || leida.Items.Count == 0)
            {
                break;
            }
        }

        return todos;
    }

    private async Task<IReadOnlyList<Orden>> OrdenesAsync(string estado)
    {
        var todas = new List<Orden>();
        for (var pagina = 1; pagina <= PaginasMaximas; pagina++)
        {
            var leida = await ordenes.ListarAsync(new FiltroOrdenes(estado, Pagina: pagina, Tamano: TamanoPagina), CancellationToken.None);
            todas.AddRange(leida.Items);
            if (todas.Count >= leida.Total || leida.Items.Count == 0)
            {
                break;
            }
        }

        return todas;
    }

    private async Task<IReadOnlyList<Orden>> OrdenesAbiertasAsync()
    {
        var abiertas = new List<Orden>();
        foreach (var estado in new[] { EstadosOrden.Asignada, EstadosOrden.EnEjecucion, EstadosOrden.Pausada, EstadosOrden.Cerrada })
        {
            abiertas.AddRange(await OrdenesAsync(estado));
        }

        return abiertas;
    }

    private async Task<IReadOnlyList<Tiquete>> TiquetesSinTecnicoAsync() =>
        (await tiquetes.ListarAsync(new FiltroTiquetes(EstadosTiquete.Recibido, Tamano: TamanoPagina), CancellationToken.None)).Items;

    // ---------------- Textos ----------------

    private static DateTimeOffset? UltimoMovimiento(Orden o) =>
        new[] { o.FechaAprobacion, o.FechaCierre, o.FechaInicio, o.FechaAsignacion }.Where(f => f is not null).Max();

    private static string Numero(int valor) => valor.ToString("N0", Cultura);

    private static string Horas(double? horas) => horas is { } h ? $"{h.ToString("0.#", Cultura)} h" : "—";

    private static string Equipos(IEnumerable<string> nombres)
    {
        var lista = nombres.Distinct().ToList();
        var texto = string.Join(" · ", lista.Take(3));
        return lista.Count > 3 ? $"{texto} y {lista.Count - 3} más" : texto;
    }

    private static string Hace(DateOnly fecha, DateOnly hoy) => (hoy.DayNumber - fecha.DayNumber) switch
    {
        <= 0 => "Hoy",
        1 => "Ayer",
        var d => $"Hace {d} días",
    };

    private static string Hace(DateTimeOffset? fecha, DateOnly hoy) =>
        fecha is { } f ? Hace(DateOnly.FromDateTime(f.LocalDateTime), hoy) : "Hoy";

    private string Relativo(DateTimeOffset momento)
    {
        var minutos = (int)(tiempo.GetUtcNow() - momento).TotalMinutes;
        return minutos switch
        {
            < 1 => "Ahora",
            < 60 => $"Hace {minutos} min",
            < 60 * 24 => $"Hace {minutos / 60} h",
            < 60 * 48 => "Ayer",
            _ => $"Hace {minutos / (60 * 24)} días",
        };
    }
}

/// <summary>Tono de una tarjeta o ícono (tokens Success, Info, Warning, High y Danger).</summary>
public enum TonoTarjeta
{
    Neutro,
    Exito,
    Info,
    Advertencia,
    Alto,
    Critico,
}

/// <summary>Indicador de la franja superior (CMP-UI-012); Destino abre la pantalla al hacer clic.</summary>
public sealed record TarjetaIndicador(string Titulo, string Valor, string Detalle, TonoTarjeta Tono, string? Destino)
{
    public bool TieneDestino => Destino is not null;
}

/// <summary>Tarjeta de «Atención requerida»: «Ver detalle» abre Destino con Parametro; «Descartar» la oculta en la sesión.</summary>
public sealed record TarjetaAtencion(
    string Clave,
    string Tipo,
    string Prioridad,
    TonoTarjeta Tono,
    string Titulo,
    string Descripcion,
    string Cuando,
    string Destino,
    string? Parametro)
{
    /// <summary>Clave del ícono de la ficha según el tipo de alerta, como en el prototipo.</summary>
    public string Icono => Tipo switch
    {
        "Mantenimiento" => "Icono.Calendario",
        "Calibración" => "Icono.Regla",
        "Orden de trabajo" => "Icono.Portapapeles",
        "Solicitud" => "Icono.Bandeja",
        "Equipo" => "Icono.Equipo",
        _ => "Icono.Alerta",
    };
}

public sealed record ActividadReciente(string Titulo, string Detalle, string Tipo, string Cuando, TonoTarjeta Tono);

public sealed record EquipoReciente(
    Guid Id,
    string Placa,
    string TipoEquipo,
    string MarcaModelo,
    string Ubicacion,
    string Estado,
    TonoEstado TonoEstado,
    string ProximoMantenimiento,
    string? AvisoProximo);

public sealed record PuntoSerie(string Etiqueta, int Preventivos, int Correctivos);

public sealed record OpcionPeriodo(string Clave, string Texto);
