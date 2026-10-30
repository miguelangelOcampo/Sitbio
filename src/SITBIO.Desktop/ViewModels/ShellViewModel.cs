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

    public ShellViewModel(string codigoRol)
    {
        _codigoRol = codigoRol;
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

    public IReadOnlyList<ModuloViewModel> Modulos { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GruposPanel), nameof(MostrarBuscador), nameof(TextoBuscador))]
    private ModuloViewModel? _moduloEnPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GruposPanel))]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    private bool _panelVisible = true;

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
        ActualizarSeleccion();
    }

    [RelayCommand]
    private void AlternarPanel() => PanelVisible = !PanelVisible;

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
