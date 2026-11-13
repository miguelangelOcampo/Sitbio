using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-009 Reportar falla (HU-003, HU-018; FLU-003, DOC-05 Figura 4). Cuatro datos: equipo (búsqueda por
/// placa o serie), tipo de falla del catálogo, criticidad con ayuda y descripción opcional de hasta 500
/// caracteres. Una falla crítica pide confirmación con MSG-006 porque el equipo quedará «Fuera de
/// servicio» (RN-005); al enviar se muestra MSG-005 con el código del tiquete. Reportan AC-02 y AC-04.
/// </summary>
public partial class ReportarFallaViewModel(IServicioTiquetes tiquetes, IServicioEquipos equipos, IPortapapeles portapapeles) : ObservableObject, ISolicitaNavegacion
{
    public const int MaximoDescripcion = 500;
    public const int MinimoBusqueda = 2;
    public const string SinEquipo = "Selecciona el equipo que presenta la falla.";

    private const string EstadoBaja = "BAJA";

    private CancellationTokenSource? _cancelacionBusqueda;
    private bool _eligiendo;

    /// <summary>Niveles de criticidad con su ayuda (CMP-UI-008: texto, no solo color).</summary>
    public static IReadOnlyList<OpcionCriticidad> OpcionesCriticidad { get; } =
    [
        new(Criticidades.Critica, "Crítica", "El equipo no puede usarse y hay riesgo para el paciente. Quedará «Fuera de servicio» hasta que se cierre la orden."),
        new(Criticidades.Alta, "Alta", "Falla una función importante; solo se puede usar con precaución o con un equipo de respaldo."),
        new(Criticidades.Media, "Media", "Falla parcial que no impide el uso habitual del equipo."),
        new(Criticidades.Baja, "Baja", "Detalle menor o estético; no afecta el funcionamiento."),
    ];

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    public bool Cargado { get; private set; }

    [ObservableProperty]
    private IReadOnlyList<string> _tiposFalla = [];

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private bool _enviando;

    [ObservableProperty]
    private Aviso? _aviso;

    // ---------- Equipo ----------

    [ObservableProperty]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCoincidencias), nameof(SinCoincidencias))]
    private IReadOnlyList<Equipo> _coincidencias = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEquipo), nameof(TextoEquipo))]
    private Equipo? _equipoSeleccionado;

    [ObservableProperty]
    private bool _buscando;

    [ObservableProperty]
    private bool _busquedaHecha;

    // ---------- Datos de la falla ----------

    [ObservableProperty]
    private string? _tipoFalla;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyudaCriticidad))]
    private OpcionCriticidad? _criticidad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContadorDescripcion))]
    private string _descripcion = string.Empty;

    [ObservableProperty]
    private string? _errorEquipo;

    [ObservableProperty]
    private string? _errorTipoFalla;

    [ObservableProperty]
    private string? _errorCriticidad;

    [ObservableProperty]
    private string? _errorDescripcion;

    // ---------- MSG-006 y MSG-005 ----------

    /// <summary>MSG-006 visible: la falla es crítica y falta confirmarla.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoConfirmacionCritica))]
    private bool _confirmandoCritica;

    /// <summary>Tiquete creado (MSG-005); null mientras se diligencia el formulario.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Enviado), nameof(Formulario), nameof(TextoResultado))]
    private Tiquete? _creado;

    [ObservableProperty]
    private string? _textoCopiado;

    public bool HayCoincidencias => Coincidencias.Count > 0;

    public bool SinCoincidencias => BusquedaHecha && !HayCoincidencias && EquipoSeleccionado is null;

    public bool HayEquipo => EquipoSeleccionado is not null;

    public string TextoEquipo => EquipoSeleccionado is { } e ? $"{e.PlacaInventario} · {e.TipoEquipo} · {e.Ubicacion} · {e.EstadoNombre}" : string.Empty;

    public string AyudaCriticidad => Criticidad?.Ayuda ?? "Elige qué tan grave es la falla; con «Crítica» el equipo queda fuera de servicio.";

    public string ContadorDescripcion => $"{Descripcion.Length}/{MaximoDescripcion}";

    public bool SinTiposFalla => Cargado && TiposFalla.Count == 0;

    public bool Enviado => Creado is not null;

    public bool Formulario => Creado is null;

    /// <summary>MSG-006 (RN-005).</summary>
    public string TextoConfirmacionCritica =>
        $"Al reportar la falla como crítica, el equipo {EquipoSeleccionado?.PlacaInventario} quedará «Fuera de servicio» hasta que se cierre la orden.";

    /// <summary>MSG-005.</summary>
    public string TextoResultado => Creado is { } t
        ? $"Reporte enviado. Su tiquete es {t.Codigo}. Úselo para consultar el estado de la atención."
        : string.Empty;

    partial void OnTiposFallaChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(SinTiposFalla));

    partial void OnBusquedaHechaChanged(bool value) => OnPropertyChanged(nameof(SinCoincidencias));

    partial void OnBusquedaChanged(string value)
    {
        if (_eligiendo)
        {
            return;
        }

        // Escribir de nuevo descarta el equipo elegido y busca otra vez.
        EquipoSeleccionado = null;
        BuscarEquiposCommand.Execute(null);
    }

    partial void OnEquipoSeleccionadoChanged(Equipo? value)
    {
        OnPropertyChanged(nameof(SinCoincidencias));
        if (value is not null)
        {
            ErrorEquipo = null;
        }
    }

    partial void OnTipoFallaChanged(string? value) => ErrorTipoFalla = null;

    partial void OnCriticidadChanged(OpcionCriticidad? value)
    {
        ErrorCriticidad = null;
        ConfirmandoCritica = false;
    }

    partial void OnDescripcionChanged(string value) =>
        ErrorDescripcion = value.Length > MaximoDescripcion ? $"La descripción admite máximo {MaximoDescripcion} caracteres." : null;

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            TiposFalla = await tiquetes.TiposFallaAsync(CancellationToken.None);
            Cargado = true;
            OnPropertyChanged(nameof(SinTiposFalla));
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir el formulario",
                error.SinConexion ? Mensajes.Msg011 : error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    /// <summary>CMP-UI-003: busca por placa y por serie (coincidencia parcial); no ofrece equipos dados de baja.</summary>
    [RelayCommand]
    private async Task BuscarEquiposAsync()
    {
        _cancelacionBusqueda?.Cancel();
        var texto = Busqueda.Trim();
        if (texto.Length < MinimoBusqueda)
        {
            Coincidencias = [];
            BusquedaHecha = false;
            return;
        }

        // La búsqueda anterior se cancela arriba y libera su propia fuente al terminar.
        using var cancelacion = new CancellationTokenSource();
        _cancelacionBusqueda = cancelacion;
        Buscando = true;
        try
        {
            var porPlaca = await equipos.ListarAsync(new FiltroEquipos(Placa: texto, Tamano: 8), cancelacion.Token);
            var porSerie = await equipos.ListarAsync(new FiltroEquipos(Serie: texto, Tamano: 8), cancelacion.Token);
            if (!cancelacion.IsCancellationRequested)
            {
                Coincidencias = porPlaca.Items.Concat(porSerie.Items)
                    .Where(e => e.Estado != EstadoBaja)
                    .DistinctBy(e => e.Id)
                    .Take(8)
                    .ToList();
                BusquedaHecha = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Llegó otra búsqueda: esta ya no importa.
        }
        catch (ErrorApi error)
        {
            Coincidencias = [];
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos buscar el equipo",
                error.SinConexion ? Mensajes.Msg011 : error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            if (cancelacion == _cancelacionBusqueda)
            {
                _cancelacionBusqueda = null;
                Buscando = false;
            }
        }
    }

    [RelayCommand]
    private void ElegirEquipo(Equipo equipo)
    {
        _eligiendo = true;
        Busqueda = equipo.PlacaInventario;
        _eligiendo = false;
        EquipoSeleccionado = equipo;
        Coincidencias = [];
        BusquedaHecha = false;
    }

    /// <summary>«Enviar reporte»: valida; si es crítica, primero pide confirmación (MSG-006).</summary>
    [RelayCommand]
    private async Task EnviarAsync()
    {
        if (!Validar())
        {
            return;
        }

        if (Criticidad!.Valor == Criticidades.Critica && !ConfirmandoCritica)
        {
            ConfirmandoCritica = true;
            return;
        }

        await Reportar();
    }

    /// <summary>MSG-006 «Reportar como crítica».</summary>
    [RelayCommand]
    private Task ConfirmarCriticaAsync() => ConfirmandoCritica && Validar() ? Reportar() : Task.CompletedTask;

    /// <summary>MSG-006 «Cambiar criticidad»: vuelve al formulario sin enviar.</summary>
    [RelayCommand]
    private void CambiarCriticidad()
    {
        ConfirmandoCritica = false;
        Criticidad = null;
    }

    [RelayCommand]
    private void Cancelar() => Limpiar();

    [RelayCommand]
    private void ReportarOtra() => Limpiar();

    /// <summary>MSG-005 «Ver seguimiento»: abre UI-010 con el tiquete recién creado (HU-017).</summary>
    [RelayCommand]
    private void VerSeguimiento()
    {
        if (Creado is { } t)
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(SeguimientoViewModel.IdPantalla, t.Codigo));
        }
    }

    [RelayCommand]
    private void CopiarCodigo()
    {
        if (Creado is { } t)
        {
            TextoCopiado = portapapeles.Copiar(t.Codigo)
                ? $"Código {t.Codigo} copiado."
                : "No se pudo copiar: otro programa está usando el portapapeles. Inténtalo de nuevo.";
        }
    }

    private bool Validar()
    {
        ErrorEquipo = EquipoSeleccionado is null ? SinEquipo : null;
        ErrorTipoFalla = string.IsNullOrWhiteSpace(TipoFalla) ? "Elige el tipo de falla." : null;
        ErrorCriticidad = Criticidad is null ? "Elige la criticidad: crítica, alta, media o baja." : null;
        OnDescripcionChanged(Descripcion);
        return ErrorEquipo is null && ErrorTipoFalla is null && ErrorCriticidad is null && ErrorDescripcion is null;
    }

    private async Task Reportar()
    {
        Enviando = true;
        try
        {
            var descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion.Trim();
            Creado = await tiquetes.ReportarAsync(new DatosTiquete(EquipoSeleccionado!.Id, TipoFalla!, descripcion, Criticidad!.Valor), CancellationToken.None);
            ConfirmandoCritica = false;
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            ConfirmandoCritica = false;
            if (error.Errores.TryGetValue("equipoId", out var equipo))
            {
                ErrorEquipo = equipo;
            }

            if (error.Errores.TryGetValue("tipoFalla", out var tipo))
            {
                ErrorTipoFalla = tipo;
            }

            if (error.Errores.TryGetValue("criticidad", out var criticidad))
            {
                ErrorCriticidad = criticidad;
            }

            if (error.Errores.TryGetValue("descripcion", out var descripcion))
            {
                ErrorDescripcion = descripcion;
            }

            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se envió el reporte",
                error.SinConexion ? Mensajes.Msg011 : error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Enviando = false;
        }
    }

    private void Limpiar()
    {
        _eligiendo = true;
        Busqueda = string.Empty;
        _eligiendo = false;
        (EquipoSeleccionado, Coincidencias, BusquedaHecha) = (null, [], false);
        (TipoFalla, Criticidad, Descripcion) = (null, null, string.Empty);
        (ErrorEquipo, ErrorTipoFalla, ErrorCriticidad, ErrorDescripcion) = (null, null, null, null);
        (ConfirmandoCritica, Creado, TextoCopiado, Aviso) = (false, null, null, null);
    }
}

/// <summary>Nivel de criticidad con su texto y su ayuda contextual.</summary>
public sealed record OpcionCriticidad(string Valor, string Texto, string Ayuda)
{
    public bool EsCritica => Valor == Criticidades.Critica;
}
