using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Marco de la sesión: riel de módulos, panel con las opciones del módulo elegido y
/// área principal. El riel marca con barra el módulo de la pantalla actual y abre en el
/// panel el módulo que se pulse; el panel se puede ocultar.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly string _codigoRol;
    private readonly IFabricaPantallas? _pantallas;
    private readonly Func<CambioClaveViewModel>? _crearCambioClave;

    public ShellViewModel(string codigoRol, IFabricaPantallas? pantallas = null, Func<CambioClaveViewModel>? crearCambioClave = null)
    {
        _codigoRol = codigoRol;
        _pantallas = pantallas;
        _crearCambioClave = crearCambioClave;
        Modulos = MenuPorRol.Para(codigoRol).Select(m => new ModuloViewModel(m)).ToList();

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
    [NotifyPropertyChangedFor(nameof(EnConstruccion))]
    private object? _contenido;

    public bool EnConstruccion => Contenido is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModuloActual), nameof(TituloModuloActual))]
    private OpcionViewModel? _opcionActual;

    /// <summary>Módulo de la pantalla actual: lleva la barra lima en el riel.</summary>
    public ModuloViewModel? ModuloActual => Modulos.FirstOrDefault(m => OpcionActual is not null && m.Opciones.Contains(OpcionActual));

    /// <summary>Para el «Overline» del área principal (se escribe en mayúsculas).</summary>
    public string TituloModuloActual => ModuloActual?.Nombre.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;

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

    /// <summary>Hay pendientes (punto rojo); se alimenta cuando existan los conteos de cada módulo.</summary>
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

    [ObservableProperty]
    private bool _esActual;
}

public sealed record GrupoOpciones(string? Titulo, IReadOnlyList<OpcionViewModel> Opciones);
