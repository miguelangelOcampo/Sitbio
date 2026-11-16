using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-019 Configuración (prototipo Admin.tsx › Configuracion): pestañas «Mi institución» (HU-024,
/// solo AC-01), «Catálogos» (UI-020, HU-025) y «Campos dinámicos» (UI-018, HU-006). El Ingeniero
/// Biomédico (AC-02) ve Catálogos y Campos dinámicos. Campos dinámicos se carga al abrir su pestaña.
/// </summary>
public partial class ConfiguracionViewModel(
    MiInstitucionViewModel? miInstitucion,
    CatalogosViewModel catalogos,
    CamposDinamicosViewModel camposDinamicos) : ObservableObject
{
    public MiInstitucionViewModel? MiInstitucion { get; } = miInstitucion;

    public CatalogosViewModel Catalogos { get; } = catalogos;

    public CamposDinamicosViewModel CamposDinamicos { get; } = camposDinamicos;

    public bool TieneMiInstitucion => MiInstitucion is not null;

    /// <summary>Contenido de la pestaña elegida.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnInstitucion), nameof(EnCatalogos), nameof(EnCampos))]
    private object _actual = (object?)miInstitucion ?? catalogos;

    public bool EnInstitucion => Actual == MiInstitucion;

    public bool EnCatalogos => Actual == Catalogos;

    public bool EnCampos => Actual == CamposDinamicos;

    [RelayCommand]
    private void MostrarInstitucion()
    {
        if (MiInstitucion is not null)
        {
            Actual = MiInstitucion;
        }
    }

    [RelayCommand]
    private void MostrarCatalogos() => Actual = Catalogos;

    [RelayCommand]
    private void MostrarCampos()
    {
        if (!CamposDinamicos.Cargado && !CamposDinamicos.Cargando)
        {
            CamposDinamicos.CargarCommand.Execute(null);
        }

        Actual = CamposDinamicos;
    }
}
