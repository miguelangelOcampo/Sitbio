using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Marco de la sesión: riel de módulos, panel con las opciones del módulo elegido y
/// área principal. El riel marca con barra el módulo de la pantalla actual y abre en el
/// panel el módulo que se pulse; el panel se puede ocultar.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    /// <summary>Pie del menú: «SITBIO v1.3.0», tomado de &lt;Version&gt; en Directory.Build.props.</summary>
    public static string VersionProducto { get; } = TextoVersion(typeof(ShellViewModel).Assembly.GetName().Version);

    public static string TextoVersion(Version? version) =>
        version is null ? "SITBIO" : $"SITBIO v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private readonly string _codigoRol;
    private readonly IFabricaPantallas? _pantallas;
    private readonly Func<CambioClaveViewModel>? _crearCambioClave;

    public ShellViewModel(
        string codigoRol,
        IFabricaPantallas? pantallas = null,
        Func<CambioClaveViewModel>? crearCambioClave = null,
        IServicioBusqueda? busqueda = null)
    {
        _codigoRol = codigoRol;
        _pantallas = pantallas;
        _crearCambioClave = crearCambioClave;
        Modulos = MenuPorRol.Para(codigoRol).Select(m => new ModuloViewModel(m)).ToList();

        // D-04: la caja de la barra superior busca en todo el sistema; la Directiva (AC-06) solo consulta el tablero.
        if (busqueda is not null && RolesBusquedaGlobal.Contains(codigoRol))
        {
            BusquedaGlobal = new BusquedaGlobalViewModel(busqueda, TieneOpcion);
            BusquedaGlobal.AperturaSolicitada += AlSolicitarNavegacion;
        }

        // UI-016: el centro de alertas se consulta al abrir la aplicación (CA-HU-020-01): vencimientos para AC-02 y AC-03
        // y, desde J-07, los cambios de los tiquetes propios para quien reporta (AC-02 y AC-04).
        if (CentroAlertasViewModel.Roles.Contains(codigoRol) && pantallas?.Crear(CentroAlertasViewModel.IdPantalla) is CentroAlertasViewModel alertas)
        {
            Alertas = alertas;
            alertas.NavegacionSolicitada += AlSolicitarNavegacion;
            alertas.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CentroAlertasViewModel.HayVencimientosPendientes))
                {
                    MarcarAviso(OpcionAlertas, alertas.HayVencimientosPendientes);
                }
            };
        }

        if (Alertas is not null)
        {
            MarcarAviso(OpcionAlertas, Alertas.HayVencimientosPendientes);
        }

        var inicio = MenuPorRol.Inicio(codigoRol);
        var opcionInicio = Modulos.SelectMany(m => m.Opciones).FirstOrDefault(o => o.Opcion.Id == inicio)
            ?? Modulos.FirstOrDefault()?.Opciones.FirstOrDefault();
        if (opcionInicio is not null)
        {
            Navegar(opcionInicio);
            ModuloEnPanel = Modulos.First(m => m.Opciones.Contains(opcionInicio));
        }
    }

    public string Rol => _codigoRol;

    /// <summary>Roles con búsqueda global (los mismos que acepta GET /busqueda).</summary>
    public static IReadOnlyList<string> RolesBusquedaGlobal { get; } = ["AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-07"];

    /// <summary>Búsqueda global de la barra superior (Ctrl + K); null si el rol no la usa.</summary>
    public BusquedaGlobalViewModel? BusquedaGlobal { get; }

    public bool TieneBusquedaGlobal => BusquedaGlobal is not null;

    /// <summary>Centro de alertas (UI-016) de la campana del riel; null para los roles que no reciben alertas.</summary>
    public CentroAlertasViewModel? Alertas { get; }

    public bool TieneAlertas => Alertas is not null;

    // ---------- I-14: punto de aviso del riel ----------

    /// <summary>Opción cuyo módulo se marca si hay alertas de vencimiento sin revisar (Mantenimientos o Calibraciones).</summary>
    public const string OpcionAlertas = "UI-015";

    /// <summary>Opción cuyo módulo se marca si hay tiquetes sin técnico asignado (la Bandeja).</summary>
    public const string OpcionBandeja = "UI-011";

    /// <summary>Tiquetes para el aviso de Solicitudes; sin servicio, el punto no se muestra.</summary>
    public IServicioTiquetes? Tiquetes { get; init; }

    /// <summary>Consulta si hay tiquetes sin técnico; si la consulta falla, el riel queda como estaba.</summary>
    [RelayCommand]
    private async Task CargarAvisosAsync()
    {
        if (Fotos is not null)
        {
            await Foto.CargarAsync(Fotos);
        }

        if (Tiquetes is null || !TieneOpcion(OpcionBandeja))
        {
            return;
        }

        try
        {
            var recibidos = await Tiquetes.ListarAsync(new FiltroTiquetes(EstadosTiquete.Recibido, Tamano: 1), CancellationToken.None);
            MarcarAviso(OpcionBandeja, recibidos.Total > 0);
        }
        catch (ErrorApi)
        {
        }
    }

    private void MarcarAviso(string idOpcion, bool hay)
    {
        foreach (var modulo in Modulos.Where(m => m.Opciones.Any(o => o.Opcion.Id == idOpcion)))
        {
            modulo.TienePendientes = hay;
        }
    }

    /// <summary>Persona de la sesión, para el avatar de la barra superior y el pie del panel (H-03).</summary>
    public SesionUsuario? Usuario { get; init; }

    /// <summary>J-12: foto de perfil del avatar, del pie del menú y del menú de la cuenta; sin foto, las iniciales.</summary>
    public FotoPerfilActual Foto { get; init; } = new();

    /// <summary>J-12: de dónde se trae la foto al entrar; null en las pruebas que no la usan.</summary>
    public IServicioFotoPerfil? Fotos { get; init; }

    /// <summary>Nombre y apellidos; si la sesión no los trae, el nombre del rol.</summary>
    public string NombreUsuario =>
        Usuario is { } u && $"{u.Nombres} {u.Apellidos}".Trim() is { Length: > 0 } nombre ? nombre : NombreRol;

    public string CorreoUsuario => Usuario?.Correo ?? string.Empty;

    /// <summary>Iniciales del avatar: primera letra del primer nombre y del primer apellido.</summary>
    /// <summary>Nombre y primer apellido («Laura Gómez Ruiz» → LG); sin datos, las del nombre mostrado.</summary>
    public string Iniciales => Usuario is { Nombres.Length: > 0, Apellidos.Length: > 0 } u
        ? InicialesDe($"{PrimeraPalabra(u.Nombres)} {PrimeraPalabra(u.Apellidos)}")
        : InicialesDe(NombreUsuario);

    private static string PrimeraPalabra(string texto) =>
        texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    public static string InicialesDe(string nombre)
    {
        var partes = (nombre ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length switch
        {
            0 => "?",
            1 => partes[0][..1].ToUpper(CultureInfo.CurrentCulture),
            _ => string.Concat(partes[0][..1], partes[^1][..1]).ToUpper(CultureInfo.CurrentCulture),
        };
    }

    /// <summary>
    /// Acción principal de la barra superior según el rol, como en el prototipo: el Ingeniero registra un equipo, el
    /// Personal clínico reporta una falla y el Administrador abre Usuarios. Se oculta en la pantalla de destino.
    /// </summary>
    public AccionBarra? AccionPrincipal => AccionBarra.Para(_codigoRol) is { } accion && TieneOpcion(accion.Destino) ? accion : null;

    public bool MostrarAccionPrincipal => AccionPrincipal is { } accion && OpcionActual?.Opcion.Id != accion.Destino;

    [RelayCommand]
    private void EjecutarAccionPrincipal()
    {
        if (AccionPrincipal is { } accion)
        {
            AlSolicitarNavegacion(this, new SolicitudNavegacion(accion.Destino, accion.Parametro));
        }
    }

    /// <summary>Nombre del rol de la sesión para el pie del panel.</summary>
    public string NombreRol => GuiaRoles.NombreDe(_codigoRol);

    /// <summary>El usuario confirmó «Cerrar sesión» (CA-HU-012-02); la ventana principal lo atiende.</summary>
    public event EventHandler? CierreSolicitado;

    [ObservableProperty]
    private bool _confirmandoCierre;

    /// <summary>UI-002 en modo voluntario, abierta como diálogo sobre el marco (HU-013).</summary>
    [ObservableProperty]
    private CambioClaveViewModel? _cambioClave;

    /// <summary>Confirmación breve sobre el área principal (por ejemplo, «Contraseña actualizada»).</summary>
    [ObservableProperty]
    private string? _confirmacion;

    public bool PuedeCambiarClave => _crearCambioClave is not null;

    [RelayCommand]
    private void AbrirCambioClave()
    {
        if (_crearCambioClave is null)
        {
            return;
        }

        var cambio = _crearCambioClave();
        cambio.Guardada += (_, _) =>
        {
            CambioClave = null;
            Confirmacion = "Contraseña actualizada. Las demás sesiones abiertas con tu cuenta se cerraron.";
        };
        cambio.Cancelada += (_, _) => CambioClave = null;
        Confirmacion = null;
        CambioClave = cambio;
    }

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    /// <summary>
    /// «Mi perfil» o «Preferencias» (D-03) desde el pie del panel. No es una opción del menú: ninguna queda marcada y el
    /// riel conserva el módulo abierto.
    /// </summary>
    [RelayCommand]
    private void AbrirPerfil(string? pestana)
    {
        var perfil = Contenido as PerfilViewModel;
        if (perfil is null)
        {
            perfil = _pantallas?.Crear(PerfilViewModel.IdPantalla) as PerfilViewModel;
            if (perfil is null)
            {
                return;
            }

            if (Contenido is ISolicitaNavegacion anterior)
            {
                anterior.NavegacionSolicitada -= AlSolicitarNavegacion;
            }

            perfil.CambioClaveSolicitado += (_, _) => AbrirCambioClave();
            if (OpcionActual is not null)
            {
                OpcionActual.EsActual = false;
                OpcionActual = null;
            }

            Contenido = perfil;
            ActualizarSeleccion();
        }

        perfil.Mostrar(pestana == "preferencias");
    }

    public bool PuedeAbrirPerfil => _pantallas is not null;

    public IReadOnlyList<ModuloViewModel> Modulos { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GruposPanel), nameof(MostrarBuscador), nameof(TextoBuscador))]
    private ModuloViewModel? _moduloEnPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GruposPanel))]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    private bool _panelVisible = true;

    /// <summary>ViewModel de la pantalla de la opción actual; null mientras la pantalla no exista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnConstruccion), nameof(TituloModuloActual), nameof(GrupoMiga), nameof(TituloMiga))]
    private object? _contenido;

    public bool EnConstruccion => Contenido is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModuloActual), nameof(TituloModuloActual), nameof(MostrarAccionPrincipal), nameof(GrupoMiga), nameof(TituloMiga))]
    private OpcionViewModel? _opcionActual;

    /// <summary>Módulo de la pantalla actual: lleva la barra lima en el riel.</summary>
    public ModuloViewModel? ModuloActual => Modulos.FirstOrDefault(m => OpcionActual is not null && m.Opciones.Contains(OpcionActual));

    /// <summary>Para el «Overline» del área principal (se escribe en mayúsculas).</summary>
    public string TituloModuloActual => Contenido is PerfilViewModel
        ? "CUENTA"
        : ModuloActual?.Nombre.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>H-03: miga de la barra superior, módulo arriba y pantalla abajo, como en el prototipo.</summary>
    public string GrupoMiga => Contenido is PerfilViewModel ? "Cuenta" : ModuloActual?.Nombre ?? string.Empty;

    public string TituloMiga => Contenido is PerfilViewModel ? "Mi perfil" : OpcionActual?.Etiqueta ?? string.Empty;

    /// <summary>Gerente, directiva y ente regulador solo consultan (RBAC): la barra lo dice junto al título.</summary>
    public bool SoloLectura => _codigoRol is "AC-05" or "AC-06" or "AC-07";

    public bool MostrarBuscador => ModuloEnPanel?.Opciones.Count > 3;

    public string TextoBuscador => $"Buscar en {ModuloEnPanel?.Corto.ToLower(CultureInfo.CurrentCulture)}…";

    /// <summary>Opciones del panel, filtradas por la búsqueda y agrupadas (Gestión: Inventario, Trabajo, Prevención).</summary>
    public IReadOnlyList<GrupoOpciones> GruposPanel
    {
        get
        {
            if (ModuloEnPanel is null)
            {
                return [];
            }

            var filtro = SinTildes(Busqueda.Trim());
            var visibles = ModuloEnPanel.Opciones
                .Where(o => filtro.Length == 0 || SinTildes(o.Opcion.Etiqueta).Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return visibles
                .GroupBy(o => o.Opcion.Subgrupo)
                .OrderBy(g => g.Key is null ? -1 : Array.IndexOf(MenuPorRol.OrdenSubgrupos, g.Key))
                .Select(g => new GrupoOpciones(g.Key?.ToUpper(CultureInfo.CurrentCulture), g.ToList()))
                .ToList();
        }
    }

    [RelayCommand]
    private void AbrirModulo(ModuloViewModel modulo)
    {
        Busqueda = string.Empty;
        ModuloEnPanel = modulo;
        PanelVisible = true;
        ActualizarSeleccion();
    }

    [RelayCommand]
    private void Navegar(OpcionViewModel opcion)
    {
        if (OpcionActual is not null)
        {
            OpcionActual.EsActual = false;
        }

        opcion.EsActual = true;
        OpcionActual = opcion;
        if (Contenido is ISolicitaNavegacion anterior)
        {
            anterior.NavegacionSolicitada -= AlSolicitarNavegacion;
        }

        Contenido = _pantallas?.Crear(opcion.Opcion.Id);
        if (Contenido is ISolicitaNavegacion nueva)
        {
            nueva.NavegacionSolicitada += AlSolicitarNavegacion;
        }

        ActualizarSeleccion();
    }

    /// <summary>Una pantalla pide abrir otra opción del menú del rol; si el rol no la tiene, no pasa nada.</summary>
    private void AlSolicitarNavegacion(object? sender, SolicitudNavegacion solicitud)
    {
        var destino = Modulos.SelectMany(m => m.Opciones).FirstOrDefault(o => o.Opcion.Id == solicitud.IdOpcion);
        if (destino is null)
        {
            return;
        }

        Navegar(destino);
        ModuloEnPanel = Modulos.First(m => m.Opciones.Contains(destino));
        if (solicitud.Parametro is { } parametro && Contenido is IRecibeParametro pantalla)
        {
            pantalla.Recibir(parametro);
        }
    }

    private bool TieneOpcion(string idOpcion) => Modulos.SelectMany(m => m.Opciones).Any(o => o.Opcion.Id == idOpcion);

    [RelayCommand]
    private void AlternarPanel() => PanelVisible = !PanelVisible;

    [RelayCommand]
    private void PedirCierre() => ConfirmandoCierre = true;

    [RelayCommand]
    private void CancelarCierre() => ConfirmandoCierre = false;

    [RelayCommand]
    private void ConfirmarCierre()
    {
        ConfirmandoCierre = false;
        CierreSolicitado?.Invoke(this, EventArgs.Empty);
    }

    private void ActualizarSeleccion()
    {
        foreach (var modulo in Modulos)
        {
            modulo.EsActivo = modulo == ModuloActual;
            modulo.EnPanel = modulo == ModuloEnPanel && PanelVisible;
        }
    }

    partial void OnPanelVisibleChanged(bool value) => ActualizarSeleccion();

    partial void OnModuloEnPanelChanged(ModuloViewModel? value) => ActualizarSeleccion();

    private static string SinTildes(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        return new string(descompuesto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}

public partial class ModuloViewModel(ModuloMenu modulo) : ObservableObject
{
    public string Nombre => modulo.Nombre;

    public string Corto => modulo.Corto;

    public string Icono => modulo.Icono;

    public IReadOnlyList<OpcionViewModel> Opciones { get; } = modulo.Opciones.Select(o => new OpcionViewModel(o)).ToList();

    /// <summary>Hay pendientes (punto rojo): alertas sin revisar o tiquetes sin técnico (I-14).</summary>
    [ObservableProperty]
    private bool _tienePendientes;

    [ObservableProperty]
    private bool _esActivo;

    [ObservableProperty]
    private bool _enPanel;
}

public partial class OpcionViewModel(OpcionMenu opcion) : ObservableObject
{
    public OpcionMenu Opcion { get; } = opcion;

    public string Etiqueta => Opcion.Etiqueta;

    /// <summary>Clave del ícono de la opción en Themes/Icons.xaml, como en el submenú del prototipo.</summary>
    public string? ClaveIcono => IconosMenu.Clave(Opcion);

    [ObservableProperty]
    private bool _esActual;
}

/// <summary>Ícono de cada opción del submenú, igual que en el prototipo (lucide).</summary>
public static class IconosMenu
{
    public static string? Clave(OpcionMenu opcion)
    {
        ArgumentNullException.ThrowIfNull(opcion);
        return (opcion.Id, opcion.Etiqueta) switch
        {
            ("UI-012", "Mis órdenes") => "Icono.ListaCheck",
            ("UI-015", "Calibraciones") => "Icono.Regla",
            ("UI-003", _) => "Icono.Panel",
            ("UI-004", _) => "Icono.Usuarios",
            ("UI-005", _) => "Icono.Equipo",
            ("UI-008", _) => "Icono.Subir",
            ("UI-009", _) => "Icono.Sirena",
            ("UI-010", _) => "Icono.Buscar",
            ("UI-011", _) => "Icono.Bandeja",
            ("UI-012", _) => "Icono.Portapapeles",
            ("UI-015", _) => "Icono.Calendario",
            ("UI-019", _) => "Icono.Ajustes",
            ("UI-021", _) => "Icono.Grafico",
            ("UI-022", _) => "Icono.Reporte",
            ("UI-023", _) => "Icono.Dispersion",
            ("UI-024", _) => "Icono.EscudoCheck",
            ("HIST", _) => "Icono.Historial",
            _ => null,
        };
    }
}

/// <summary>Acción principal de la barra superior: texto, ícono (Themes/Icons.xaml), pantalla y parámetro.</summary>
public sealed record AccionBarra(string Texto, string Icono, string Destino, string? Parametro = null)
{
    public static AccionBarra? Para(string codigoRol) => codigoRol switch
    {
        "AC-02" => new("Registrar equipo", "Icono.Agregar", "UI-005", EquiposViewModel.ParametroNuevo),
        "AC-04" => new("Reportar falla", "Icono.Sirena", "UI-009"),
        "AC-01" => new("Usuarios", "Icono.Usuarios", "UI-004"),
        _ => null,
    };
}

public sealed record GrupoOpciones(string? Titulo, IReadOnlyList<OpcionViewModel> Opciones);
