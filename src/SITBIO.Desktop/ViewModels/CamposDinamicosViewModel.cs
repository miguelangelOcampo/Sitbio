using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-018 Campos dinámicos (HU-006, RF-006; prototipo Admin.tsx › CamposDinamicos), pestaña de UI-019
/// Configuración. Por tipo de equipo se ve la versión publicada y una vista previa de cómo aparecerán
/// los campos en la intervención (UI-013). El Ingeniero Biomédico (AC-02) arma un borrador (agregar y
/// quitar campos) y lo publica como versión nueva; el Administrador (AC-01) solo consulta
/// (CA-HU-006-03). Las intervenciones ya registradas conservan la versión con la que se guardaron.
/// </summary>
public partial class CamposDinamicosViewModel(
    IServicioConfiguracionCampos servicio,
    IServicioCatalogos catalogos,
    SesionActual sesion) : ObservableObject
{
    /// <summary>Tipos de dato con su texto (prototipo: TIPO_DATO).</summary>
    public static IReadOnlyList<OpcionFiltro> OpcionesTipoDato { get; } =
    [
        new(TiposDatoCampo.Numero, "Número entero"),
        new(TiposDatoCampo.NumeroDecimal, "Número decimal"),
        new(TiposDatoCampo.Texto, "Texto"),
        new(TiposDatoCampo.Booleano, "Sí / No"),
        new(TiposDatoCampo.Fecha, "Fecha"),
        new(TiposDatoCampo.Seleccion, "Selección"),
    ];

    // Borradores sin publicar por tipo de equipo, como el prototipo: cambiar de tipo no los pierde.
    private readonly Dictionary<Guid, List<CampoDinamico>> _borradores = [];

    public bool Cargado { get; private set; }

    [ObservableProperty]
    private IReadOnlyList<ElementoCatalogo> _tiposEquipo = [];

    [ObservableProperty]
    private ElementoCatalogo? _tipoSeleccionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoVersion), nameof(TextoPublicar), nameof(TieneVersion))]
    private ConfiguracionCampos? _publicada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCampos), nameof(SinCampos), nameof(TextoVacio), nameof(HayCambios))]
    [NotifyCanExecuteChangedFor(nameof(PublicarCommand), nameof(DescartarCommand))]
    private IReadOnlyList<FilaCampo> _campos = [];

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublicarCommand))]
    private bool _publicando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    public bool PuedeGestionar => sesion.Sesion?.Rol == "AC-02";

    public bool HayCampos => Campos.Count > 0;

    public bool SinCampos => !HayCampos && TipoSeleccionado is not null;

    public bool TieneVersion => Publicada?.Version is not null;

    public bool HayCambios => TipoSeleccionado is { } t && _borradores.ContainsKey(t.Id);

    public string TextoVersion => Publicada?.Version is { } v ? $"Versión publicada v{v}" : "Sin campos publicados";

    public string TextoPublicar => $"Publicar v{(Publicada?.Version ?? 0) + 1}";

    public string TextoVacio => TipoSeleccionado is not { } tipo ? string.Empty
        : PuedeGestionar ? $"{tipo.Nombre} aún no tiene campos técnicos. Agrega el primero para que el técnico lo diligencie en la intervención."
        : $"{tipo.Nombre} aún no tiene campos técnicos. El Ingeniero Biomédico puede definirlos.";

    // ---------- Nuevo campo ----------

    [ObservableProperty]
    private bool _agregando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NombreInterno))]
    private string _nuevaEtiqueta = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NuevoEsNumerico), nameof(NuevoEsSeleccion))]
    private OpcionFiltro _nuevoTipoDato = OpcionesTipoDato[0];

    [ObservableProperty]
    private string _nuevaUnidad = string.Empty;

    [ObservableProperty]
    private string _nuevasOpciones = string.Empty;

    [ObservableProperty]
    private bool _nuevoObligatorio;

    [ObservableProperty]
    private string? _errorEtiqueta;

    [ObservableProperty]
    private string? _errorOpciones;

    public bool NuevoEsNumerico => TiposDatoCampo.EsNumerico(NuevoTipoDato.Valor);

    public bool NuevoEsSeleccion => NuevoTipoDato.Valor == TiposDatoCampo.Seleccion;

    public string NombreInterno => NombreDesde(NuevaEtiqueta) is { Length: > 0 } nombre ? $"Nombre interno: {nombre}" : "Lo que verá el técnico.";

    partial void OnNuevaEtiquetaChanged(string value) => ErrorEtiqueta = null;

    partial void OnNuevasOpcionesChanged(string value) => ErrorOpciones = null;

    partial void OnTipoSeleccionadoChanged(ElementoCatalogo? value)
    {
        Agregando = false;
        Confirmacion = null;
        if (value is not null)
        {
            CargarTipoCommand.Execute(null);
        }
    }

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            var anterior = TipoSeleccionado?.Id;
            TiposEquipo = (await catalogos.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None)).Where(t => t.Activo).ToList();
            Cargado = true;
            Aviso = null;
            TipoSeleccionado = TiposEquipo.FirstOrDefault(t => t.Id == anterior) ?? TiposEquipo.FirstOrDefault();
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir los campos dinámicos",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private async Task CargarTipoAsync()
    {
        if (TipoSeleccionado is not { } tipo)
        {
            return;
        }

        Cargando = true;
        try
        {
            var configuracion = await servicio.ObtenerAsync(tipo.Id, CancellationToken.None);
            if (TipoSeleccionado?.Id == tipo.Id)
            {
                Publicada = configuracion;
                Mostrar();
                Aviso = null;
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir los campos de este tipo",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private void AbrirAgregar()
    {
        (NuevaEtiqueta, NuevoTipoDato, NuevaUnidad, NuevasOpciones, NuevoObligatorio) = (string.Empty, OpcionesTipoDato[0], string.Empty, string.Empty, false);
        (ErrorEtiqueta, ErrorOpciones) = (null, null);
        Agregando = PuedeGestionar && TipoSeleccionado is not null;
    }

    [RelayCommand]
    private void CancelarAgregar() => Agregando = false;

    /// <summary>Agrega el campo al borrador (no publica). Mismas reglas que la API (ReglasCamposDinamicos).</summary>
    [RelayCommand]
    private void AgregarCampo()
    {
        if (!PuedeGestionar || TipoSeleccionado is not { } tipo)
        {
            return;
        }

        var etiqueta = NuevaEtiqueta.Trim();
        var nombre = NombreDesde(etiqueta);
        var actuales = Borrador(tipo);
        ErrorEtiqueta = etiqueta.Length < 3 ? "Escribe la etiqueta visible."
            : etiqueta.Length > 80 ? "La etiqueta admite máximo 80 caracteres."
            : actuales.Any(c => c.Nombre == nombre) ? "Ya hay un campo con ese nombre."
            : null;
        var opciones = NuevasOpciones.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ErrorOpciones = NuevoEsSeleccion && opciones.Count < 2 ? "Escribe al menos dos opciones separadas por coma." : null;
        if (ErrorEtiqueta is not null || ErrorOpciones is not null)
        {
            return;
        }

        var unidad = NuevoEsNumerico && NuevaUnidad.Trim() is { Length: > 0 } u ? u : null;
        actuales.Add(new CampoDinamico(nombre, etiqueta, NuevoTipoDato.Valor, NuevoObligatorio, NuevoEsSeleccion ? opciones : null, unidad));
        _borradores[tipo.Id] = actuales;
        Agregando = false;
        Confirmacion = null;
        Mostrar();
    }

    [RelayCommand]
    private void Quitar(FilaCampo fila)
    {
        if (!PuedeGestionar || TipoSeleccionado is not { } tipo)
        {
            return;
        }

        var actuales = Borrador(tipo);
        actuales.RemoveAll(c => c.Nombre == fila.Campo.Nombre);
        _borradores[tipo.Id] = actuales;
        Mostrar();
    }

    [RelayCommand(CanExecute = nameof(PuedeDescartar))]
    private void Descartar()
    {
        if (TipoSeleccionado is { } tipo)
        {
            _borradores.Remove(tipo.Id);
            Mostrar();
        }
    }

    private bool PuedeDescartar() => HayCambios;

    [RelayCommand(CanExecute = nameof(PuedePublicar))]
    private async Task PublicarAsync()
    {
        if (TipoSeleccionado is not { } tipo || !_borradores.TryGetValue(tipo.Id, out var borrador))
        {
            return;
        }

        Publicando = true;
        try
        {
            var publicada = await servicio.PublicarAsync(tipo.Id, borrador, CancellationToken.None);
            _borradores.Remove(tipo.Id);
            Publicada = publicada;
            Mostrar();
            Aviso = null;
            Confirmacion = new Aviso(
                $"Versión {publicada.Version} publicada",
                "Las nuevas intervenciones usarán estos campos. Las ya registradas conservan la versión con la que se guardaron.",
                null,
                EsError: false);
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo publicar la versión",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Publicando = false;
        }
    }

    private bool PuedePublicar() => PuedeGestionar && HayCambios && HayCampos && !Publicando;

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    private List<CampoDinamico> Borrador(ElementoCatalogo tipo) =>
        _borradores.TryGetValue(tipo.Id, out var borrador) ? [.. borrador] : [.. Publicada?.Campos ?? []];

    private void Mostrar()
    {
        var lista = TipoSeleccionado is { } tipo && _borradores.TryGetValue(tipo.Id, out var borrador) ? borrador : (IReadOnlyList<CampoDinamico>)(Publicada?.Campos ?? []);
        Campos = lista.Select(c => new FilaCampo(c)).ToList();
        OnPropertyChanged(nameof(HayCambios));
        PublicarCommand.NotifyCanExecuteChanged();
        DescartarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clave en minúsculas sin tildes con guion bajo; la misma regla que la API (ReglasCamposDinamicos.NombreDesde).</summary>
    public static string NombreDesde(string etiqueta)
    {
        var sinTildes = new StringBuilder();
        foreach (var letra in etiqueta.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra) != UnicodeCategory.NonSpacingMark)
            {
                sinTildes.Append(letra);
            }
        }

        var nombre = NoAlfanumerico().Replace(sinTildes.ToString().ToLowerInvariant(), "_").Trim('_');
        if (nombre.Length > 0 && char.IsDigit(nombre[0]))
        {
            nombre = "campo_" + nombre;
        }

        return nombre.Length > 50 ? nombre[..50].TrimEnd('_') : nombre;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NoAlfanumerico();
}

/// <summary>Fila de la tabla de campos y de la vista previa de la intervención.</summary>
public sealed record FilaCampo(CampoDinamico Campo)
{
    public string Etiqueta => Campo.Etiqueta;

    public string Nombre => Campo.Nombre;

    public string EtiquetaConUnidad => Campo.Unidad is { Length: > 0 } u ? $"{Campo.Etiqueta} ({u})" : Campo.Etiqueta;

    public string TipoDato => CamposDinamicosViewModel.OpcionesTipoDato.FirstOrDefault(o => o.Valor == Campo.TipoDato)?.Texto ?? Campo.TipoDato;

    public string Obligatorio => Campo.Obligatorio ? "Sí" : "No";

    public string Opciones => Campo.Opciones is { Count: > 0 } o ? string.Join(" · ", o) : string.Empty;

    public bool EsBooleano => Campo.TipoDato == TiposDatoCampo.Booleano;

    public bool EsSeleccion => Campo.TipoDato == TiposDatoCampo.Seleccion;

    public bool EsTextoLibre => !EsBooleano && !EsSeleccion;

    /// <summary>Texto de ayuda de la caja de la vista previa (prototipo).</summary>
    public string Marcador => Campo.TipoDato switch
    {
        TiposDatoCampo.Fecha => "dd/mm/aaaa",
        TiposDatoCampo.Texto => "Texto",
        _ => "0",
    };
}
