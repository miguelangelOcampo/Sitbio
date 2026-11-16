using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-014 Revisión y firma (HU-021, FLU-006). El Ingeniero Biomédico (AC-02) revisa en solo lectura la
/// intervención de una orden «cerrada» y la aprueba con su firma. Antes de firmar confirma MSG-018; al
/// aprobar, la orden y su intervención quedan inalterables (RN-004) y la pantalla vuelve a UI-012 con la
/// orden «aprobada». Si la calibración del equipo venció y sale como «Operativo», la API lo impide (RN-007,
/// MSG-013).
/// </summary>
public partial class RevisionViewModel : ObservableObject
{
    public const string IdPantalla = "UI-014";
    public const string FirmaObligatoria = "Escribe tu firma para aprobar la orden.";

    private readonly IServicioOrdenes _ordenes;
    private readonly Action<Orden?> _volver;

    public RevisionViewModel(
        IServicioOrdenes ordenes,
        IServicioEquipos equipos,
        IServicioConfiguracionCampos configuracion,
        Orden orden,
        Action<Orden?> volver)
    {
        _ordenes = ordenes;
        _volver = volver;
        Orden = orden;
        Detalle = new IntervencionViewModel(ordenes, equipos, configuracion, orden, editable: false, _ => { }) { MuestraBarra = false };
    }

    public Orden Orden { get; }

    /// <summary>La intervención en solo lectura, sin su barra de acciones.</summary>
    public IntervencionViewModel Detalle { get; }

    public string Referencia => Orden.CodigoTiquete ?? Orden.PlacaInventario;

    public string Tecnico => Orden.Tecnico;

    public string Cerrada => Orden.FechaCierre is { } f
        ? f.ToLocalTime().ToString("d MMM yyyy, HH:mm", TextosEquipo.Cultura)
        : "Sin fecha";

    /// <summary>MSG-018.</summary>
    public string TextoConfirmacion =>
        $"Al aprobar y firmar, la orden {Referencia} y su intervención quedarán inalterables. ¿Desea continuar?";

    public int LongitudMaximaFirma => CodigosOrden.LongitudMaximaFirma;

    [ObservableProperty]
    private string _firma = string.Empty;

    [ObservableProperty]
    private string? _errorFirma;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private bool _confirmando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AprobarCommand), nameof(ConfirmarAprobacionCommand))]
    private bool _aprobando;

    /// <summary>Falso si la API respondió que ya está aprobada (RN-004) o que no está cerrada.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AprobarCommand), nameof(ConfirmarAprobacionCommand))]
    private bool _puedeAprobar = true;

    [RelayCommand]
    private Task CargarAsync() => Detalle.CargarCommand.ExecuteAsync(null);

    partial void OnFirmaChanged(string value) => ErrorFirma = null;

    /// <summary>«Aprobar y firmar»: valida la firma y pide la confirmación MSG-018.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAprobar))]
    private void Aprobar()
    {
        if (ValidarFirma())
        {
            Confirmando = true;
        }
    }

    [RelayCommand]
    private void CancelarAprobacion() => Confirmando = false;

    /// <summary>MSG-018 «Aprobar y firmar».</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAprobar))]
    private async Task ConfirmarAprobacionAsync()
    {
        Confirmando = false;
        if (!ValidarFirma())
        {
            return;
        }

        Aprobando = true;
        Aviso = null;
        try
        {
            _volver(await _ordenes.AprobarAsync(Orden.Id, Firma.Trim(), CancellationToken.None));
        }
        catch (ErrorApi error)
        {
            if (error.Errores.TryGetValue("firma", out var mensaje))
            {
                ErrorFirma = mensaje;
                return;
            }

            Aviso = error.Codigo switch
            {
                CodigosOrden.CalibracionVencida => new Aviso("Calibración vencida", error.Message, error.Codigo),
                CodigosOrden.Firmada => new Aviso("La orden ya fue aprobada", error.Message, error.Codigo),
                _ => new Aviso(
                    error.SinConexion ? "Sin conexión" : $"No se pudo aprobar la orden {Referencia}",
                    error.Message,
                    error.SinConexion ? "MSG-011" : error.Codigo),
            };
            if (error.Codigo is CodigosOrden.Firmada or "OT-002")
            {
                PuedeAprobar = false;
            }
        }
        finally
        {
            Aprobando = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => _volver(null);

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    private bool SePuedeAprobar() => PuedeAprobar && !Aprobando;

    private bool ValidarFirma()
    {
        var firma = Firma.Trim();
        ErrorFirma = firma.Length == 0 ? FirmaObligatoria
            : firma.Length > CodigosOrden.LongitudMaximaFirma ? $"La firma admite hasta {CodigosOrden.LongitudMaximaFirma} caracteres."
            : null;
        return ErrorFirma is null;
    }
}
