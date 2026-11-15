using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public partial class ReportesViewModel(IServicioReportes servicio) : ObservableObject
{
    public const string IdPantalla = "UI-021";

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
        nameof(Costo), nameof(DetalleCosto), nameof(Fuera), nameof(DetalleFuera), nameof(AvisoCostos))]
    private Indicadores? _indicadores;

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

/// <summary>
/// UI-022 Exportación (HU-007, HU-027): el historial de órdenes del rango en PDF o Excel (CA-HU-007-01/02) para
/// AC-01, AC-02 y AC-05, y la hoja de vida de un equipo en PDF (CA-HU-027-01), que también genera AC-07.
/// </summary>
public partial class ExportacionViewModel(IServicioReportes servicio, IServicioEquipos equipos, IDialogoArchivos dialogo, SesionActual sesion) : ObservableObject
{
    public const string IdPantalla = "UI-022";

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

    [ObservableProperty]
    private string _placa = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinResultados))]
    private IReadOnlyList<Equipo>? _resultados;

    [ObservableProperty]
    private Equipo? _equipoElegido;

    public bool SinResultados => Resultados is { Count: 0 };

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

    [RelayCommand]
    private async Task BuscarEquipoAsync()
    {
        if (string.IsNullOrWhiteSpace(Placa))
        {
            Resultados = null;
            return;
        }

        try
        {
            Resultados = (await equipos.ListarAsync(new FiltroEquipos(Placa: Placa, Tamano: 10), CancellationToken.None)).Items;
            EquipoElegido = Resultados.Count == 1 ? Resultados[0] : null;
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos buscar el equipo", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
    }

    [RelayCommand]
    private void ElegirEquipo(Equipo equipo) => EquipoElegido = equipo;

    [RelayCommand]
    private async Task GenerarHojaVidaAsync()
    {
        if (EquipoElegido is not { } equipo)
        {
            Aviso = new Aviso("Elige un equipo", "Busca el equipo por su placa y elígelo antes de generar la hoja de vida.", null);
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
