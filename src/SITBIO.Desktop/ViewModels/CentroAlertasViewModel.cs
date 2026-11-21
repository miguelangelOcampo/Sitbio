using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-016 Centro de alertas (HU-020; FLU-008): panel lateral que se abre desde la campana del riel. Para el Ingeniero
/// Biomédico (AC-02) y el Técnico (AC-03) se consulta al abrir la aplicación (CA-HU-020-01) y muestra los
/// mantenimientos preventivos y las calibraciones que entran en los días de anticipación de la institución o que ya
/// vencieron (CA-HU-020-02), las vencidas primero. Al consultar, la API genera las órdenes preventivas de los planes
/// cuya fecha llegó (CA-HU-019-02). Marcar una alerta como revisada la saca del conteo durante la sesión; abrirla
/// lleva a UI-015 y cierra el panel (NAV-011).
/// J-07 (HU-017): para quien reporta fallas (AC-02 y AC-04) la campana también muestra los cambios de estado de sus
/// propios tiquetes desde la última vez que los vio (fecha guardada en las preferencias de la estación, por usuario).
/// Abrir el panel los da por vistos; abrir uno lleva a UI-010 con ese tiquete.
/// </summary>
public partial class CentroAlertasViewModel(
    IServicioMantenimiento servicio,
    IServicioTiquetes? tiquetes = null,
    SesionActual? sesion = null,
    IServicioPreferencias? preferencias = null,
    TimeProvider? tiempo = null) : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "UI-016";

    /// <summary>Días hacia atrás de los cambios de tiquetes que se consultan (los mismos que cubre la API).</summary>
    public const int DiasCambios = 30;

    /// <summary>Roles con campana: los que reciben alertas de vencimiento y los que reportan fallas.</summary>
    public static IReadOnlyList<string> Roles { get; } = ["AC-02", "AC-03", "AC-04"];

    /// <summary>Roles que reciben alertas de vencimiento (DOC-05, UI-016).</summary>
    public static IReadOnlyList<string> RolesVencimientos { get; } = ["AC-02", "AC-03"];

    /// <summary>Roles que reportan fallas y reciben los cambios de sus tiquetes (J-07).</summary>
    public static IReadOnlyList<string> RolesMisTiquetes { get; } = ["AC-02", "AC-04"];

    private readonly TimeProvider _tiempo = tiempo ?? TimeProvider.System;
    private readonly HashSet<string> _revisadas = [];
    private int _consulta;
    private int _consultaCambios;

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    private string? Rol => sesion?.Sesion?.Rol;

    private Guid? UsuarioId => sesion?.UsuarioId;

    /// <summary>Alertas de mantenimiento y calibración: AC-02 y AC-03 (sin sesión, como antes de J-07).</summary>
    public bool VeVencimientos => Rol is null || RolesVencimientos.Contains(Rol);

    /// <summary>Cambios de los tiquetes propios: quien reporta, si hay servicio de tiquetes.</summary>
    public bool VeMisTiquetes => tiquetes is not null && (Rol is null || RolesMisTiquetes.Contains(Rol));

    public string Titulo => VeVencimientos ? "Centro de alertas" : "Notificaciones";

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
    [NotifyPropertyChangedFor(nameof(SinAlertas), nameof(Pendientes), nameof(HayPendientes), nameof(TextoPendientes), nameof(HayRevisadas), nameof(HayVencimientosPendientes))]
    private IReadOnlyList<FilaAlerta> _filas = [];

    /// <summary>Órdenes preventivas que generó la última consulta.</summary>
    [ObservableProperty]
    private string? _ordenesGeneradas;

    // ---------- J-07: cambios de los tiquetes propios ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCambios), nameof(SinCambios))]
    private IReadOnlyList<FilaCambioTiquete> _cambios = [];

    /// <summary>Cambios que llegaron después de la última vez que el usuario abrió la campana.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Pendientes), nameof(HayPendientes), nameof(TextoPendientes), nameof(HayCambiosSinLeer))]
    private int _cambiosSinLeer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCambios))]
    private Aviso? _avisoCambios;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCambios))]
    private bool _cambiosCargados;

    public bool Cargado { get; private set; }

    public bool SinAlertas => VeVencimientos && Cargado && !Cargando && Aviso is null && Filas.Count == 0;

    public bool HayCambios => Cambios.Count > 0;

    public bool HayCambiosSinLeer => CambiosSinLeer > 0;

    public bool SinCambios => CambiosCargados && AvisoCambios is null && Cambios.Count == 0;

    /// <summary>Alertas de vencimiento sin revisar: marcan el módulo de Mantenimientos en el riel.</summary>
    public bool HayVencimientosPendientes => Filas.Any(f => !f.Revisada);

    /// <summary>El número de la campana: alertas sin revisar y cambios de tiquetes sin leer.</summary>
    public int Pendientes => Filas.Count(f => !f.Revisada) + CambiosSinLeer;

    public bool HayPendientes => Pendientes > 0;

    public bool HayRevisadas => Filas.Any(f => f.Revisada);

    public string TextoPendientes => (VeVencimientos, Pendientes) switch
    {
        (true, 0) => "Sin alertas pendientes",
        (true, 1) => "1 alerta pendiente",
        (true, var n) => $"{n} alertas pendientes",
        (false, 0) => "Sin novedades de tus tiquetes",
        (false, 1) => "1 novedad de tus tiquetes",
        (false, var n) => $"{n} novedades de tus tiquetes",
    };

    public string TextoAnticipacion => $"Mantenimientos y calibraciones de los próximos {DiasAnticipacion} días y los vencidos.";

    [RelayCommand]
    private async Task CargarAsync()
    {
        var cambios = VeMisTiquetes ? CargarCambiosAsync() : Task.CompletedTask;
        if (VeVencimientos)
        {
            await CargarVencimientosAsync();
        }
        else
        {
            Cargado = true;
        }

        await cambios;
    }

    private async Task CargarVencimientosAsync()
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

    /// <summary>J-07: cambios de los tiquetes propios; los posteriores a la última revisión cuentan como nuevos.</summary>
    private async Task CargarCambiosAsync()
    {
        var consulta = ++_consultaCambios;
        try
        {
            var ahora = _tiempo.GetUtcNow();
            var lista = await tiquetes!.MisCambiosAsync(ahora.AddDays(-DiasCambios), CancellationToken.None);
            if (consulta != _consultaCambios)
            {
                return;
            }

            var vista = UsuarioId is { } usuario ? preferencias?.Leer().RevisionDe(usuario) : null;
            Cambios = lista.Select(c => new FilaCambioTiquete(c, vista is null || c.FechaCambio > vista, ahora)).ToList();
            CambiosSinLeer = Cambios.Count(c => c.Nuevo);
            AvisoCambios = null;
            if (Abierto)
            {
                MarcarCambiosVistos();
            }
        }
        catch (ErrorApi error)
        {
            if (consulta != _consultaCambios)
            {
                return;
            }

            Cambios = [];
            CambiosSinLeer = 0;
            AvisoCambios = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar tus tiquetes",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consultaCambios)
            {
                CambiosCargados = true;
            }
        }
    }

    /// <summary>Guarda como vista la fecha del cambio más reciente y quita el número; las filas siguen marcadas como nuevas.</summary>
    private void MarcarCambiosVistos()
    {
        CambiosSinLeer = 0;
        if (Cambios.Count == 0 || preferencias is null || UsuarioId is not { } usuario)
        {
            return;
        }

        var ultimo = Cambios.Max(c => c.Cambio.FechaCambio);
        var actuales = preferencias.Leer();
        if (actuales.RevisionDe(usuario) is { } anterior && anterior >= ultimo)
        {
            return;
        }

        preferencias.Guardar(actuales.ConRevision(usuario, ultimo));
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

    /// <summary>J-07: abre el tiquete en UI-010 y cierra el panel.</summary>
    [RelayCommand]
    private void AbrirCambio(FilaCambioTiquete? fila)
    {
        if (fila is null)
        {
            return;
        }

        Abierto = false;
        NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(SeguimientoViewModel.IdPantalla, fila.Cambio.Codigo));
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

    /// <summary>Días desde los que una alerta próxima es de prioridad media.</summary>
    public const int DiasPrioridadMedia = 7;

    // ---------- I-11: tarjeta con el diseño del prototipo (Panel, «Atención requerida») ----------

    /// <summary>Ícono del tipo de alerta en la ficha.</summary>
    public string Icono => Alerta.Tipo == TiposAlerta.Calibracion ? "Icono.Regla" : "Icono.Calendario";

    /// <summary>Vencida: alta; vence dentro de una semana: media; más adelante: baja.</summary>
    public string Prioridad => Vencida ? "Prioridad alta" : Alerta.DiasRestantes <= DiasPrioridadMedia ? "Prioridad media" : "Prioridad baja";

    /// <summary>Color del punto de la prioridad (no cambia al descartarla).</summary>
    public TonoEstado TonoPrioridad => Vencida ? TonoEstado.Critico : Alerta.DiasRestantes <= DiasPrioridadMedia ? TonoEstado.Advertencia : TonoEstado.Neutro;

    /// <summary>Tiempo relativo: «Vence en 3 días», «Vencida ayer».</summary>
    public string Cuando => Estado;

    public string Titulo => Equipo;

    /// <summary>Qué vence, dónde está el equipo y la fecha.</summary>
    public string Detalle => $"{Descripcion} · {Ubicacion} · {Fecha}";

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

/// <summary>J-07: un cambio de estado de un tiquete propio, como lo muestra la campana.</summary>
public sealed record FilaCambioTiquete(CambioMiTiquete Cambio, bool Nuevo, DateTimeOffset Ahora)
{
    public string Titulo => $"{Cambio.Codigo} · {Cambio.TipoEquipo}";

    public string Equipo => Cambio.PlacaInventario;

    public string Estado => EstadosTiquete.Nombre(Cambio.NuevoEstado);

    /// <summary>Qué pasó con el tiquete, en palabras de quien lo reportó.</summary>
    public string Texto => Cambio.NuevoEstado switch
    {
        EstadosTiquete.Asignado => "Se asignó un técnico a tu reporte.",
        EstadosTiquete.EnEjecucion => "El técnico empezó a atender la falla.",
        EstadosTiquete.Cerrado => "La atención terminó y el tiquete se cerró.",
        EstadosTiquete.Recibido => "Tu reporte volvió a quedar recibido.",
        _ => $"Pasó a «{Estado}».",
    };

    public string? Comentario => string.IsNullOrWhiteSpace(Cambio.Comentario) ? null : Cambio.Comentario;

    public string Cuando => Relativo(Cambio.FechaCambio, Ahora);

    public string Detalle => $"{Cambio.Usuario} · {Cuando}";

    /// <summary>«Hace un momento», «Hace 5 min», «Hace 3 h», «Ayer», «Hace 4 días».</summary>
    public static string Relativo(DateTimeOffset fecha, DateTimeOffset ahora)
    {
        var lapso = ahora - fecha;
        return lapso switch
        {
            { TotalMinutes: < 1 } => "Hace un momento",
            { TotalMinutes: < 60 } => $"Hace {(int)lapso.TotalMinutes} min",
            { TotalHours: < 24 } => $"Hace {(int)lapso.TotalHours} h",
            { TotalDays: < 2 } => "Ayer",
            _ => $"Hace {(int)lapso.TotalDays} días",
        };
    }
}
