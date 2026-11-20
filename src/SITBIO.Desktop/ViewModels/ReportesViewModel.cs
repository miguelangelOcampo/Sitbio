using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Fechas dd/mm/aaaa escritas en los filtros de UI-021 y UI-022.</summary>
public static class FechasFiltro
{
    private static readonly string[] Formatos = ["d/M/yyyy", "d-M-yyyy", "yyyy-M-d"];

    /// <summary>true si el texto está vacío (null) o es una fecha válida.</summary>
    public static bool Leer(string texto, out DateOnly? fecha)
    {
        fecha = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (DateOnly.TryParseExact(texto.Trim(), Formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor))
        {
            fecha = valor;
            return true;
        }

        return false;
    }

    public static string Texto(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// UI-021 Reportes e indicadores (HU-026): los cinco indicadores de la institución en el periodo
/// (CA-HU-026-01). Solo consulta: la Directiva (AC-06) no tiene más acciones (CA-HU-026-02).
/// </summary>
public partial class ReportesViewModel(IServicioReportes servicio, SesionActual? sesion = null) : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "UI-021";

    /// <summary>Meses del gráfico «Mantenimientos por mes» (prototipo: 3, 6 o 12 meses).</summary>
    public const int MesesPorDefecto = 6;

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    [ObservableProperty]
    private string _desde = string.Empty;

    [ObservableProperty]
    private string _hasta = string.Empty;

    [ObservableProperty]
    private string? _errorFechas;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HayDatos), nameof(Periodo), nameof(Cumplimiento), nameof(DetalleCumplimiento), nameof(Mttr), nameof(Mtbf),
        nameof(Costo), nameof(DetalleCosto), nameof(Fuera), nameof(DetalleFuera), nameof(AvisoCostos), nameof(MiniDisponibilidad))]
    private Indicadores? _indicadores;

    // ---------------- Gráficos (I-02, prototipo Tablero) ----------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meses3), nameof(Meses6), nameof(Meses12))]
    private int _mesesSerie = MesesPorDefecto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaGrafico))]
    private bool _vistaTabla;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HaySerie), nameof(EtiquetasSerie), nameof(SeriesMantenimientos), nameof(FilasSerie), nameof(MiniCumplimiento), nameof(MiniMttr))]
    private IReadOnlyList<MesIndicadores> _serie = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HayParque), nameof(SeriesParque), nameof(TextoOperativos), nameof(Operativos), nameof(EnMantenimiento),
        nameof(FueraDeServicio), nameof(EquiposFuera), nameof(SinEquiposFuera))]
    private Parque? _parque;

    [ObservableProperty]
    private Aviso? _avisoGraficos;

    public bool Meses3
    {
        get => MesesSerie == 3;
        set => ElegirMeses(value, 3);
    }

    public bool Meses6
    {
        get => MesesSerie == 6;
        set => ElegirMeses(value, 6);
    }

    public bool Meses12
    {
        get => MesesSerie == 12;
        set => ElegirMeses(value, 12);
    }

    /// <summary>«Vista: Gráfico / Tabla de datos»; la tabla es la alternativa accesible del gráfico.</summary>
    public bool VistaGrafico
    {
        get => !VistaTabla;
        set => VistaTabla = !value;
    }

    public bool HaySerie => Serie.Count > 0;

    public IReadOnlyList<string> EtiquetasSerie => Serie.Select(m => NombreMes(m.Mes)).ToList();

    public IReadOnlyList<SerieGrafico> SeriesMantenimientos =>
    [
        new SerieGrafico("Preventivos", "Brush.Primary", Serie.Select(m => (double)m.Preventivos).ToList()),
        new SerieGrafico("Correctivos", "Brush.Jade", Serie.Select(m => (double)m.Correctivos).ToList(), Discontinua: true),
    ];

    public IReadOnlyList<FilaMes> FilasSerie => Serie
        .Select(m => new FilaMes(MesCompleto(m.Mes), m.Preventivos, m.Correctivos, m.Preventivos + m.Correctivos))
        .ToList();

    /// <summary>Mini barras del cumplimiento preventivo por mes (%).</summary>
    public IReadOnlyList<SerieGrafico> MiniCumplimiento =>
        [new SerieGrafico("Cumplimiento (%)", "Brush.Primary", Serie.Select(m => m.CumplimientoPreventivo ?? 0).ToList())];

    /// <summary>Mini línea del MTTR por mes (horas).</summary>
    public IReadOnlyList<SerieGrafico> MiniMttr =>
        [new SerieGrafico("MTTR (h)", "Brush.Jade", Serie.Select(m => m.MttrHoras ?? 0).ToList())];

    /// <summary>Mini arco: equipos disponibles frente a fuera de servicio.</summary>
    public IReadOnlyList<SerieGrafico> MiniDisponibilidad => Indicadores?.FueraDeServicio is { EquiposActivos: > 0 } f
        ? [new SerieGrafico("Disponibles", "Brush.Primary", [f.EquiposActivos - f.Equipos]), new SerieGrafico("Fuera de servicio", "Brush.Danger", [f.Equipos])]
        : [];

    public bool HayParque => Parque is not null;

    public int Operativos => Parque?.Operativos ?? 0;

    public int EnMantenimiento => Parque?.EnMantenimiento ?? 0;

    public int FueraDeServicio => Parque?.FueraDeServicio ?? 0;

    public IReadOnlyList<SerieGrafico> SeriesParque => Parque is { } p
        ?
        [
            new SerieGrafico("Operativo", "Brush.Success", [p.Operativos]),
            new SerieGrafico("En mantenimiento", "Brush.Warning", [p.EnMantenimiento]),
            new SerieGrafico("Fuera de servicio", "Brush.Danger", [p.FueraDeServicio]),
        ]
        : [];

    public string TextoOperativos => Parque is { } p && p.Operativos + p.EnMantenimiento + p.FueraDeServicio is > 0 and var total
        ? $"{Math.Round(100.0 * p.Operativos / total).ToString("0", Cultura)} %"
        : "—";

    public IReadOnlyList<EquipoFuera> EquiposFuera => Parque?.EquiposFuera ?? [];

    public bool SinEquiposFuera => Parque is { EquiposFuera.Count: 0 };

    /// <summary>Solo los roles con Equipos biomédicos en su menú abren la hoja de vida (la Directiva no).</summary>
    public bool PuedeAbrirEquipos => sesion?.Sesion?.Rol is { } rol && MenuPorRol.Para(rol).SelectMany(m => m.Opciones).Any(o => o.Id == EquiposViewModel.IdPantalla);

    [RelayCommand]
    private void AbrirEquipo(EquipoFuera? equipo)
    {
        if (equipo is not null && PuedeAbrirEquipos)
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(EquiposViewModel.IdPantalla, EquiposViewModel.ParametroDetalle(equipo.Id, equipo.PlacaInventario)));
        }
    }

    private void ElegirMeses(bool elegido, int meses)
    {
        if (elegido && MesesSerie != meses)
        {
            MesesSerie = meses;
            _ = CargarSerieAsync();
        }
    }

    [RelayCommand]
    private async Task CargarSerieAsync()
    {
        try
        {
            Serie = await servicio.SerieIndicadoresAsync(MesesSerie, CancellationToken.None);
            AvisoGraficos = null;
        }
        catch (ErrorApi error)
        {
            AvisoGraficos = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos cargar los gráficos", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
    }

    private async Task CargarParqueAsync()
    {
        try
        {
            Parque = await servicio.ParqueAsync(CancellationToken.None);
        }
        catch (ErrorApi error)
        {
            AvisoGraficos = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos cargar el estado del parque", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
    }

    private static string NombreMes(DateOnly mes) => mes.ToString("MMM", Cultura).TrimEnd('.');

    private static string MesCompleto(DateOnly mes)
    {
        var texto = mes.ToString("MMMM yyyy", Cultura);
        return char.ToUpper(texto[0], Cultura) + texto[1..];
    }

    public bool HayDatos => Indicadores is not null;

    public string Periodo => Indicadores is { } k ? $"Del {FechasFiltro.Texto(k.Desde)} al {FechasFiltro.Texto(k.Hasta)}" : string.Empty;

    public string Cumplimiento => Porcentaje(Indicadores?.CumplimientoPreventivo.Porcentaje);

    public string DetalleCumplimiento => Indicadores?.CumplimientoPreventivo is { } c
        ? c.Programadas == 0 ? "Sin preventivas programadas" : $"{c.Cumplidas} de {c.Programadas} preventivas cerradas"
        : string.Empty;

    public string Mttr => Horas(Indicadores?.MttrHoras);

    public string Mtbf => Horas(Indicadores?.MtbfHoras);

    public string Costo => Indicadores?.CostoPorEquipo.Promedio is { } p ? p.ToString("C0", Cultura) : "—";

    public string DetalleCosto => Indicadores?.CostoPorEquipo is { } c
        ? c.CostoTotal is { } total ? $"{total.ToString("C0", Cultura)} entre {c.EquiposActivos} equipos activos" : "Costo no disponible"
        : string.Empty;

    public string Fuera => Indicadores?.FueraDeServicio.Equipos.ToString(Cultura) ?? "—";

    public string DetalleFuera => Indicadores?.FueraDeServicio is { } f
        ? f.Porcentaje is { } p ? $"{p.ToString("0.#", Cultura)} % de {f.EquiposActivos} equipos activos" : "Sin equipos activos"
        : string.Empty;

    public Aviso? AvisoCostos => Indicadores is { CostosCompletos: false }
        ? new Aviso("Costo no disponible", "El repositorio de intervenciones no respondió; los demás indicadores están completos.", "MSG-011", PuedeReintentar: true)
        : null;

    [RelayCommand]
    private async Task CargarAsync()
    {
        if (!FechasFiltro.Leer(Desde, out var desde) || !FechasFiltro.Leer(Hasta, out var hasta))
        {
            ErrorFechas = "Escribe las fechas como dd/mm/aaaa.";
            return;
        }

        if (desde is { } d && hasta is { } h && h < d)
        {
            ErrorFechas = "La fecha final no puede ser anterior a la inicial.";
            return;
        }

        ErrorFechas = null;
        Cargando = true;
        try
        {
            Indicadores = await servicio.IndicadoresAsync(desde, hasta, CancellationToken.None);
            Aviso = null;
            await CargarSerieAsync();
            await CargarParqueAsync();
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos cargar los indicadores", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    private static string Porcentaje(double? valor) => valor is { } v ? $"{v.ToString("0.#", Cultura)} %" : "—";

    private static string Horas(double? valor) => valor is { } v ? $"{v.ToString("0.#", Cultura)} h" : "—";
}

/// <summary>Fila de la «Tabla de datos» de «Mantenimientos por mes».</summary>
public sealed record FilaMes(string Mes, int Preventivos, int Correctivos, int Total);

/// <summary>
/// UI-022 Exportación (HU-007, HU-027): el historial de órdenes del rango en PDF o Excel (CA-HU-007-01/02) para
/// AC-01, AC-02 y AC-05, y la hoja de vida de un equipo en PDF (CA-HU-027-01), que también genera AC-07.
/// </summary>
public partial class ExportacionViewModel(
    IServicioReportes servicio, IServicioEquipos equipos, IDialogoArchivos dialogo, SesionActual sesion, TimeSpan? retardoBusqueda = null) : ObservableObject
{
    public const string IdPantalla = "UI-022";

    /// <summary>Caracteres desde los que se sugieren equipos (I-08).</summary>
    public const int MinimoBusqueda = 2;

    /// <summary>Equipos que se sugieren como máximo.</summary>
    public const int MaximoSugerencias = 8;

    private readonly TimeSpan _retardo = retardoBusqueda ?? TimeSpan.FromMilliseconds(300);
    private int _consulta;
    private bool _eligiendo;

    public static IReadOnlyList<OpcionFiltro> Formatos { get; } = [new("pdf", "PDF"), new("xlsx", "Excel (.xlsx)")];

    /// <summary>El historial lo exportan AC-01, AC-02 y AC-05 (API-REP); AC-07 solo la hoja de vida.</summary>
    public bool PuedeExportarHistorial => sesion.Sesion?.Rol is "AC-01" or "AC-02" or "AC-05";

    // ---------- Historial ----------

    [ObservableProperty]
    private string _desde = string.Empty;

    [ObservableProperty]
    private string _hasta = string.Empty;

    [ObservableProperty]
    private OpcionFiltro _formato = Formatos[0];

    [ObservableProperty]
    private string? _errorFechas;

    // ---------- Hoja de vida ----------

    /// <summary>Placa, serie o nombre del equipo; desde dos caracteres sugiere coincidencias (I-08).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlaca))]
    private string _placa = string.Empty;

    public bool SinPlaca => string.IsNullOrEmpty(Placa);

    /// <summary>Equipos sugeridos para lo escrito; null mientras no se ha consultado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinResultados), nameof(HaySugerencias))]
    private IReadOnlyList<Equipo>? _resultados;

    /// <summary>La sugerencia resaltada con las flechas; Enter la elige.</summary>
    [ObservableProperty]
    private Equipo? _sugerido;

    [ObservableProperty]
    private bool _buscandoEquipo;

    [ObservableProperty]
    private Equipo? _equipoElegido;

    public bool SinResultados => Resultados is { Count: 0 };

    public bool HaySugerencias => Resultados is { Count: > 0 };

    /// <summary>La última búsqueda lanzada al escribir (con su retardo); permite esperarla.</summary>
    public Task BusquedaEnCurso { get; private set; } = Task.CompletedTask;

    // ---------- Estado ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Libre))]
    private bool _ocupado;

    public bool Libre => !Ocupado;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    /// <summary>Por defecto, el mes en curso hasta hoy.</summary>
    [RelayCommand]
    private void Cargar()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        Desde = FechasFiltro.Texto(new DateOnly(hoy.Year, hoy.Month, 1));
        Hasta = FechasFiltro.Texto(hoy);
    }

    [RelayCommand]
    private async Task ExportarHistorialAsync()
    {
        if (!FechasFiltro.Leer(Desde, out var desde) || !FechasFiltro.Leer(Hasta, out var hasta) || desde is null || hasta is null)
        {
            ErrorFechas = "Indica las dos fechas como dd/mm/aaaa.";
            return;
        }

        if (hasta < desde)
        {
            ErrorFechas = "La fecha final no puede ser anterior a la inicial.";
            return;
        }

        ErrorFechas = null;
        await DescargarAsync(() => servicio.HistorialOrdenesAsync(desde.Value, hasta.Value, Formato.Valor, CancellationToken.None), "No pudimos exportar el historial");
    }

    partial void OnPlacaChanged(string value)
    {
        if (_eligiendo)
        {
            return;
        }

        // Escribir de nuevo descarta el equipo elegido y vuelve a sugerir.
        EquipoElegido = null;
        BusquedaEnCurso = SugerirAsync(value.Trim(), conRetardo: true);
    }

    /// <summary>Busca de inmediato lo escrito (Enter sin sugerencias o «Reintentar»).</summary>
    [RelayCommand]
    private Task BuscarEquipoAsync() => BusquedaEnCurso = SugerirAsync(Placa.Trim(), conRetardo: false);

    /// <summary>
    /// Sugerencias por placa, serie o nombre (tipo, marca o modelo) con la búsqueda parcial de GET /equipos; con
    /// retardo para no consultar en cada tecla. Una consulta vieja que termina tarde no pisa a la nueva.
    /// </summary>
    private async Task SugerirAsync(string texto, bool conRetardo)
    {
        var consulta = ++_consulta;
        if (texto.Length < MinimoBusqueda)
        {
            Resultados = null;
            Sugerido = null;
            BuscandoEquipo = false;
            return;
        }

        BuscandoEquipo = true;
        try
        {
            if (conRetardo && _retardo > TimeSpan.Zero)
            {
                await Task.Delay(_retardo);
                if (consulta != _consulta)
                {
                    return;
                }
            }

            var consultas = new[]
            {
                new FiltroEquipos(Placa: texto, Tamano: MaximoSugerencias),
                new FiltroEquipos(Serie: texto, Tamano: MaximoSugerencias),
                new FiltroEquipos(Texto: texto, Tamano: MaximoSugerencias),
            }.Select(f => equipos.ListarAsync(f, CancellationToken.None));
            var paginas = await Task.WhenAll(consultas);
            if (consulta != _consulta)
            {
                return;
            }

            Resultados = paginas.SelectMany(p => p.Items).DistinctBy(e => e.Id).Take(MaximoSugerencias).ToList();
            Sugerido = Resultados.FirstOrDefault();
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            if (consulta == _consulta)
            {
                Resultados = null;
                Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos buscar el equipo", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
            }
        }
        finally
        {
            if (consulta == _consulta)
            {
                BuscandoEquipo = false;
            }
        }
    }

    [RelayCommand]
    private void SugerenciaSiguiente() => MoverSugerencia(1);

    [RelayCommand]
    private void SugerenciaAnterior() => MoverSugerencia(-1);

    private void MoverSugerencia(int paso)
    {
        if (Resultados is not { Count: > 0 } lista)
        {
            return;
        }

        var actual = Sugerido is null ? -1 : lista.ToList().IndexOf(Sugerido);
        Sugerido = lista[((actual + paso) % lista.Count + lista.Count) % lista.Count];
    }

    /// <summary>Elige el equipo (clic o Enter sobre el resaltado): escribe su placa y cierra las sugerencias.</summary>
    [RelayCommand]
    private async Task ElegirEquipoAsync(Equipo? equipo)
    {
        if ((equipo ?? Sugerido) is not { } elegido)
        {
            // Enter antes de que lleguen las sugerencias: se busca ya y, si hay una sola, se elige.
            await BuscarEquipoAsync();
            if (Resultados is [var unico])
            {
                elegido = unico;
            }
            else
            {
                return;
            }
        }

        _consulta++;
        _eligiendo = true;
        try
        {
            Placa = elegido.PlacaInventario;
        }
        finally
        {
            _eligiendo = false;
        }

        EquipoElegido = elegido;
        Resultados = null;
        Sugerido = null;
        BuscandoEquipo = false;
    }

    /// <summary>Esc cierra las sugerencias sin elegir.</summary>
    [RelayCommand]
    private void CerrarSugerencias()
    {
        _consulta++;
        Resultados = null;
        Sugerido = null;
        BuscandoEquipo = false;
    }

    [RelayCommand]
    private async Task GenerarHojaVidaAsync()
    {
        if (EquipoElegido is not { } equipo)
        {
            Aviso = new Aviso("Elige un equipo", "Escribe la placa, la serie o el nombre del equipo y elígelo en la lista antes de generar la hoja de vida.", null);
            return;
        }

        await DescargarAsync(() => servicio.HojaVidaAsync(equipo.Id, CancellationToken.None), "No pudimos generar la hoja de vida");
    }

    private async Task DescargarAsync(Func<Task<ArchivoDescargado>> descargar, string tituloError)
    {
        Ocupado = true;
        Confirmacion = null;
        try
        {
            var archivo = await descargar();
            var ruta = dialogo.ElegirDestino(archivo.NombreArchivo);
            if (ruta is null)
            {
                return;
            }

            dialogo.Guardar(ruta, archivo.Contenido);
            Aviso = null;
            Confirmacion = new Aviso("Archivo guardado", ruta, null, EsError: false);
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(error.SinConexion ? "Sin conexión" : tituloError, error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        catch (System.IO.IOException error)
        {
            Aviso = new Aviso("No se pudo guardar el archivo", error.Message, null);
        }
        catch (UnauthorizedAccessException)
        {
            Aviso = new Aviso("No se pudo guardar el archivo", "No tienes permiso para escribir en esa carpeta. Elige otra.", null);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
