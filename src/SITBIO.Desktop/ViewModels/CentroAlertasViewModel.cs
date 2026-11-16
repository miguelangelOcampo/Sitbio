using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-016 Centro de alertas (HU-020; FLU-008): panel lateral que se abre desde la campana del riel, solo para el
/// Ingeniero Biomédico (AC-02) y el Técnico (AC-03). Se consulta al abrir la aplicación (CA-HU-020-01) y muestra los
/// mantenimientos preventivos y las calibraciones que entran en los días de anticipación de la institución o que ya
/// vencieron (CA-HU-020-02), las vencidas primero. Al consultar, la API genera las órdenes preventivas de los planes
/// cuya fecha llegó (CA-HU-019-02). Marcar una alerta como revisada la saca del conteo durante la sesión; abrirla
/// lleva a UI-015 y cierra el panel (NAV-011).
/// </summary>
public partial class CentroAlertasViewModel(IServicioMantenimiento servicio) : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "UI-016";

    /// <summary>Roles que reciben alertas (DOC-05, UI-016).</summary>
    public static IReadOnlyList<string> Roles { get; } = ["AC-02", "AC-03"];

    private readonly HashSet<string> _revisadas = [];
    private int _consulta;

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    [ObservableProperty]
    private bool _abierto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinAlertas))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinAlertas))]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAnticipacion))]
    private int _diasAnticipacion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinAlertas), nameof(Pendientes), nameof(HayPendientes), nameof(TextoPendientes), nameof(HayRevisadas))]
    private IReadOnlyList<FilaAlerta> _filas = [];

    /// <summary>Órdenes preventivas que generó la última consulta.</summary>
    [ObservableProperty]
    private string? _ordenesGeneradas;

    public bool Cargado { get; private set; }

    public bool SinAlertas => Cargado && !Cargando && Aviso is null && Filas.Count == 0;

    /// <summary>Alertas sin revisar: el número de la campana.</summary>
    public int Pendientes => Filas.Count(f => !f.Revisada);

    public bool HayPendientes => Pendientes > 0;

    public bool HayRevisadas => Filas.Any(f => f.Revisada);

    public string TextoPendientes => Pendientes switch
    {
        0 => "Sin alertas pendientes",
        1 => "1 alerta pendiente",
        _ => $"{Pendientes} alertas pendientes",
    };

    public string TextoAnticipacion => $"Mantenimientos y calibraciones de los próximos {DiasAnticipacion} días y los vencidos.";

    [RelayCommand]
    private async Task CargarAsync()
    {
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resumen = await servicio.AlertasAsync(CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            DiasAnticipacion = resumen.DiasAnticipacion;
            Filas = resumen.Alertas.Select(a => new FilaAlerta(a, _revisadas.Contains(a.Clave))).ToList();
            OrdenesGeneradas = resumen.OrdenesGeneradas switch
            {
                0 => null,
                1 => "Se generó 1 orden preventiva de un plan cuya fecha llegó; ya está en el panel del técnico.",
                var n => $"Se generaron {n} órdenes preventivas de planes cuya fecha llegó; ya están en el panel de los técnicos.",
            };
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            if (consulta != _consulta)
            {
                return;
            }

            Filas = [];
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar las alertas",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargado = true;
                Cargando = false;
                OnPropertyChanged(nameof(SinAlertas));
            }
        }
    }

    /// <summary>La campana abre el panel y vuelve a consultar; pulsarla de nuevo lo cierra.</summary>
    [RelayCommand]
    private void Alternar()
    {
        Abierto = !Abierto;
        if (Abierto)
        {
            CargarCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void Cerrar() => Abierto = false;

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand]
    private void CerrarOrdenesGeneradas() => OrdenesGeneradas = null;

    [RelayCommand]
    private void MarcarRevisada(FilaAlerta? fila)
    {
        if (fila is null || fila.Revisada)
        {
            return;
        }

        _revisadas.Add(fila.Alerta.Clave);
        Filas = Filas.Select(f => f == fila ? f with { Revisada = true } : f).ToList();
    }

    [RelayCommand]
    private void MarcarTodasRevisadas()
    {
        foreach (var fila in Filas)
        {
            _revisadas.Add(fila.Alerta.Clave);
        }

        Filas = Filas.Select(f => f with { Revisada = true }).ToList();
    }

    /// <summary>NAV-011: abre el plan en UI-015 (o la pestaña de calibraciones) y cierra el panel.</summary>
    [RelayCommand]
    private void AbrirAlerta(FilaAlerta? fila)
    {
        if (fila is null)
        {
            return;
        }

        var parametro = fila.Alerta.PlanId is { } plan
            ? $"{MantenimientosViewModel.PrefijoPlan}{plan}"
            : $"{MantenimientosViewModel.PrefijoCalibracion}{fila.Alerta.EquipoId}";
        Abierto = false;
        NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(MantenimientosViewModel.IdPantalla, parametro));
    }
}

/// <summary>Tarjeta de alerta (CMP-UI de DOC-05): tipo, equipo, fecha y estado con punto y texto (ACC-001).</summary>
public sealed record FilaAlerta(Alerta Alerta, bool Revisada)
{
    public bool Vencida => Alerta.Estado == TiposAlerta.Vencida;

    public string Tipo => Alerta.Tipo == TiposAlerta.Calibracion ? "Calibración" : "Mantenimiento preventivo";

    public string Equipo => $"{Alerta.PlacaInventario} · {Alerta.TipoEquipo}";

    public string Ubicacion => Alerta.Ubicacion;

    public string Descripcion => Alerta.Tipo == TiposAlerta.Calibracion ? "Vence la calibración" : Alerta.Descripcion;

    public string Fecha => TextosEquipo.Fecha(Alerta.Fecha);

    public string Estado => Alerta.DiasRestantes switch
    {
        < -1 => $"Vencida hace {-Alerta.DiasRestantes} días",
        -1 => "Vencida ayer",
        0 => "Vence hoy",
        1 => "Vence mañana",
        var d => $"Vence en {d} días",
    };

    public TonoEstado Tono => Revisada ? TonoEstado.Neutro : Vencida ? TonoEstado.Critico : TonoEstado.Advertencia;

    /// <summary>La orden preventiva que generó el plan, si ya existe.</summary>
    public string? Orden => Alerta.OrdenId is null ? null : $"Orden preventiva {EstadoOrden(Alerta.EstadoOrden)}";

    private static string EstadoOrden(string? estado) => estado switch
    {
        "asignada" => "asignada",
        "en_ejecucion" => "en ejecución",
        "pausada" => "pausada",
        "cerrada" => "cerrada, por aprobar",
        "aprobada" => "aprobada",
        _ => estado ?? string.Empty,
    };
}
