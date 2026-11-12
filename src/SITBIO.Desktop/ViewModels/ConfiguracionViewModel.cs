using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-019 Configuración (prototipo Admin.tsx › Configuracion): pestañas «Mi institución» (HU-024,
/// solo AC-01) y «Catálogos» (UI-020, HU-025). El Ingeniero Biomédico (AC-02) solo ve Catálogos.
/// «Campos dinámicos» (UI-018) llega con HU-006.
/// </summary>
public partial class ConfiguracionViewModel(MiInstitucionViewModel? miInstitucion, CatalogosViewModel catalogos) : ObservableObject
{
    public MiInstitucionViewModel? MiInstitucion { get; } = miInstitucion;

    public CatalogosViewModel Catalogos { get; } = catalogos;

    public bool TieneMiInstitucion => MiInstitucion is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnCatalogos), nameof(Actual))]
    private bool _enInstitucion = miInstitucion is not null;

    public bool EnCatalogos => !EnInstitucion;

    /// <summary>Contenido de la pestaña elegida.</summary>
    public object Actual => EnInstitucion && MiInstitucion is not null ? MiInstitucion : Catalogos;

    [RelayCommand]
    private void MostrarInstitucion() => EnInstitucion = MiInstitucion is not null;

    [RelayCommand]
    private void MostrarCatalogos() => EnInstitucion = false;
}
