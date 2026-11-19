using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Pestaña «Mantenimientos» de la hoja de vida UI-006 (prototipo HojaVida.tsx, sección 3): el plan preventivo
/// vigente (HU-019), las órdenes de trabajo del equipo (API-OT) y sus calibraciones con la vigencia o «No aplica»
/// (HU-022, RN-010). Las tres consultas van en paralelo y cada sección muestra su propio error. Cada sección solo
/// se pide si el rol la puede consultar (la API también lo valida, RNF-003): las órdenes no las ve AC-05 y el
/// Técnico (AC-03) solo ve las suyas.
/// </summary>
public partial class MantenimientosEquipoViewModel(
    IServicioMantenimiento mantenimiento,
    IServicioOrdenes ordenes,
    TimeProvider tiempo,
    Guid equipoId,
    bool requiereCalibracion,
    string? rol) : ObservableObject
{
    /// <summary>Órdenes y calibraciones que se traen; la hoja de vida muestra las más recientes.</summary>
    public const int Limite = 50;

    /// <summary>Roles que consultan los planes y las calibraciones (API-PLAN y API-CAL).</summary>
    public static IReadOnlyList<string> RolesMantenimiento { get; } = ["AC-01", "AC-02", "AC-03", "AC-05", "AC-07"];

    /// <summary>Roles que consultan las órdenes de trabajo (API-OT).</summary>
    public static IReadOnlyList<string> RolesOrdenes { get; } = ["AC-01", "AC-02", "AC-03", "AC-07"];

    public static bool PuedeVer(string? rol) => rol is not null && RolesMantenimiento.Contains(rol);

    public bool PuedeVerOrdenes => rol is not null && RolesOrdenes.Contains(rol);

    /// <summary>AC-03: la API solo le devuelve las órdenes asignadas a él.</summary>
    public bool SoloSuyas => rol == "AC-03";

    public bool RequiereCalibracion => requiereCalibracion;

    private DateOnly Hoy => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlan), nameof(SinOrdenes), nameof(SinCalibraciones))]
    private bool _cargando;

    /// <summary>Ya se consultó al menos una vez (la pestaña no vuelve a pedir los datos al abrirse de nuevo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlan), nameof(SinOrdenes), nameof(SinCalibraciones))]
    private bool _cargado;

    // ---------- Plan preventivo vigente (HU-019) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayPlan), nameof(SinPlan), nameof(Periodicidad), nameof(UltimaEjecucion), nameof(ProximaFecha), nameof(DetalleProxima), nameof(PlanVencido), nameof(DescripcionPlan))]
    private PlanMantenimiento? _plan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlan))]
    private Aviso? _avisoPlan;

    public bool HayPlan => Plan is not null;

    public bool SinPlan => Cargado && !Cargando && Plan is null && AvisoPlan is null;

    public string Periodicidad => Plan is { } p ? TextosEquipo.Periodicidad(p.PeriodicidadDias) : TextosEquipo.SinDato;

    public string UltimaEjecucion => Plan?.UltimaEjecucion is { } u ? TextosEquipo.Fecha(u) : "Sin ejecuciones";

    public string ProximaFecha => Plan is { } p ? TextosEquipo.Fecha(p.ProximaFecha) : TextosEquipo.SinDato;

    public string DetalleProxima => Plan is not { } p ? string.Empty : p.Vencido ? $"Vencido · {TextosEquipo.DiasHasta(p.ProximaFecha, Hoy)}" : TextosEquipo.DiasHasta(p.ProximaFecha, Hoy);

    public bool PlanVencido => Plan?.Vencido == true;

    public string DescripcionPlan => Plan?.Descripcion ?? string.Empty;

    // ---------- Órdenes de trabajo del equipo ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinOrdenes), nameof(ResumenOrdenes))]
    private IReadOnlyList<FilaOrdenEquipo>? _ordenesEquipo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenOrdenes))]
    private int _totalOrdenes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinOrdenes))]
    private Aviso? _avisoOrdenes;

    public bool SinOrdenes => !Cargando && OrdenesEquipo is { Count: 0 } && AvisoOrdenes is null;

    public string ResumenOrdenes => OrdenesEquipo is not { Count: > 0 } filas
        ? string.Empty
        : TotalOrdenes > filas.Count
            ? $"Las {filas.Count} más recientes de {TotalOrdenes}"
            : filas.Count == 1 ? "1 orden" : $"{filas.Count} órdenes";

    // ---------- Calibraciones (HU-022, RN-010) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCalibraciones), nameof(Vigencia), nameof(DetalleVigencia), nameof(TonoVigencia))]
    private IReadOnlyList<FilaCalibracion>? _calibraciones;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCalibraciones))]
    private Aviso? _avisoCalibraciones;

    public bool SinCalibraciones => RequiereCalibracion && !Cargando && Calibraciones is { Count: 0 } && AvisoCalibraciones is null;

    /// <summary>La calibración más reciente decide la vigencia del equipo; «No aplica» si no la requiere (RN-010).</summary>
    public string Vigencia => !RequiereCalibracion
        ? "No aplica"
        : Calibraciones switch
        {
            null => TextosEquipo.SinDato,
            [] => "Sin calibraciones",
            [var ultima, ..] => ultima.Calibracion.Vigente ? "Vigente" : "Vencida",
        };

    public string DetalleVigencia => !RequiereCalibracion
        ? "El equipo no requiere calibración"
        : Calibraciones is [var ultima, ..]
            ? ultima.Calibracion.Vigente ? $"Hasta {ultima.Vence}" : $"Venció el {ultima.Vence}"
            : string.Empty;

    public TonoEstado TonoVigencia => !RequiereCalibracion
        ? TonoEstado.Neutro
        : Calibraciones switch
        {
            [var ultima, ..] => ultima.Tono,
            [] => TonoEstado.Advertencia,
            _ => TonoEstado.Neutro,
        };

    public bool HayAviso => AvisoPlan is not null || AvisoOrdenes is not null || AvisoCalibraciones is not null;

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            await Task.WhenAll(CargarPlanAsync(), CargarOrdenesAsync(), CargarCalibracionesAsync());
        }
        finally
        {
            Cargado = true;
            Cargando = false;
            OnPropertyChanged(nameof(HayAviso));
        }
    }

    private async Task CargarPlanAsync()
    {
        try
        {
            var planes = await mantenimiento.ListarPlanesAsync(new FiltroPlanes(equipoId, EstadosPlan.Activo, 1, 5), CancellationToken.None);
            Plan = planes.Items.FirstOrDefault();
            AvisoPlan = null;
        }
        catch (ErrorApi error)
        {
            AvisoPlan = AvisoDe(error, "No pudimos consultar el plan preventivo");
        }
    }

    private async Task CargarOrdenesAsync()
    {
        if (!PuedeVerOrdenes)
        {
            return;
        }

        try
        {
            var pagina = await ordenes.ListarAsync(new FiltroOrdenes(Tamano: Limite, EquipoId: equipoId), CancellationToken.None);
            OrdenesEquipo = pagina.Items.Select(o => new FilaOrdenEquipo(o)).ToList();
            TotalOrdenes = pagina.Total;
            AvisoOrdenes = null;
        }
        catch (ErrorApi error)
        {
            AvisoOrdenes = AvisoDe(error, "No pudimos consultar las órdenes");
        }
    }

    private async Task CargarCalibracionesAsync()
    {
        if (!RequiereCalibracion)
        {
            return;
        }

        try
        {
            var pagina = await mantenimiento.ListarCalibracionesAsync(new FiltroCalibraciones(equipoId, 1, Limite), CancellationToken.None);
            Calibraciones = pagina.Items.Select(c => new FilaCalibracion(c, Hoy, RolCorrige: false, RolAprueba: false)).ToList();
            AvisoCalibraciones = null;
        }
        catch (ErrorApi error)
        {
            AvisoCalibraciones = AvisoDe(error, "No pudimos consultar las calibraciones");
        }
    }

    private static Aviso AvisoDe(ErrorApi error, string titulo) => new(
        error.SinConexion ? "Sin conexión" : titulo,
        error.Message,
        error.SinConexion ? "MSG-011" : error.Codigo,
        PuedeReintentar: true);
}

/// <summary>Orden de trabajo en la hoja de vida: fecha, tipo, origen, técnico y estado con punto (ACC-001).</summary>
public sealed record FilaOrdenEquipo(Orden Orden)
{
    public string Fecha => (Orden.FechaCreacion ?? Orden.FechaAsignacion) is { } fecha
        ? fecha.ToLocalTime().ToString("dd/MM/yyyy", TextosEquipo.Cultura)
        : TextosEquipo.Fecha(Orden.FechaProgramada);

    public string Tipo => Orden.TipoMantenimiento switch
    {
        "preventivo" => "Preventivo",
        "correctivo" => "Correctivo",
        var otro => otro,
    };

    /// <summary>Código del tiquete de origen; las preventivas salen del plan.</summary>
    public string Origen => Orden.CodigoTiquete ?? (Orden.TipoMantenimiento == "preventivo" ? "Plan preventivo" : TextosEquipo.SinDato);

    public string Detalle => Orden.TipoFalla ?? "Mantenimiento programado";

    public string Tecnico => Orden.Tecnico;

    public string Estado => EstadosOrden.Nombre(Orden.Estado);

    public TonoEstado Tono => Orden.Estado switch
    {
        EstadosOrden.EnEjecucion => TonoEstado.Advertencia,
        EstadosOrden.Aprobada or EstadosOrden.Cerrada => TonoEstado.Exito,
        _ => TonoEstado.Neutro,
    };
}
