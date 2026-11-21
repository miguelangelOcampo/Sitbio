using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Diálogo «Agregar o cambiar fotografía» de la hoja de vida (RF-031, solo AC-02; prototipo
/// <c>components/FotoEquipo.tsx</c>). La imagen se reduce aquí a 128 y 512 px antes de enviarla: el original no se sube.
/// Para guardar hay que confirmar el aviso de datos personales (Ley 1581 de 2012).
/// </summary>
public partial class FotoEquipoViewModel(
    IServicioFotoEquipo servicio,
    IReductorFoto reductor,
    IDialogoArchivos dialogo,
    Guid equipoId,
    string placa,
    byte[]? vistaActual) : ObservableObject
{
    /// <summary>JPG, PNG o WebP de hasta 10 MB (como el prototipo); lo que se sube pesa mucho menos.</summary>
    public const long TamanoMaximoOriginal = 10L * 1024 * 1024;

    public const string FiltroImagenes = "Imágenes (*.jpg; *.jpeg; *.png; *.webp)|*.jpg;*.jpeg;*.png;*.webp";

    public const string AvisoDatosPersonales =
        "La foto debe mostrar solo el equipo, sin pacientes, historias clínicas ni otros datos personales (Ley 1581 de 2012). "
        + "SITBIO la reduce a 128 px y 512 px antes de enviarla; el original no se guarda.";

    public static readonly IReadOnlyList<string> Extensiones = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Al cerrar: la nueva miniatura (null si se quitó) cuando hubo cambio; sin cambio, no se invoca.</summary>
    public event EventHandler<CambioFoto>? Cambiada;

    /// <summary>Se cierra sin cambios (Cancelar).</summary>
    public event EventHandler? Cancelado;

    public string Placa { get; } = placa;

    public bool TieneFoto { get; } = vistaActual is not null;

    public string Titulo => TieneFoto ? $"Cambiar fotografía de {Placa}" : $"Agregar fotografía a {Placa}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia), nameof(HayVistaPrevia), nameof(TextoPeso), nameof(PuedeQuitar))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FotoReducida? _nueva;

    private long _tamanoOriginal;

    /// <summary>La foto nueva si se eligió; si no, la actual (512 px).</summary>
    public byte[]? VistaPrevia => Nueva?.Vista ?? vistaActual;

    public bool HayVistaPrevia => VistaPrevia is not null;

    /// <summary>«Original: 3.412 KB → guardada: 46 KB».</summary>
    public string TextoPeso => Nueva is null
        ? string.Empty
        : $"Original: {Kb(_tamanoOriginal)} KB → guardada: {Kb(Nueva.TamanoBytes)} KB";

    /// <summary>«Quitar fotografía» solo con una foto guardada y sin haber elegido otra.</summary>
    public bool PuedeQuitar => TieneFoto && Nueva is null;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _avisoAceptado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand), nameof(QuitarCommand))]
    private bool _ocupado;

    [ObservableProperty]
    private Aviso? _aviso;

    [RelayCommand]
    private void ElegirImagen()
    {
        var ruta = dialogo.ElegirArchivo(Titulo, FiltroImagenes);
        if (ruta is null)
        {
            return;
        }

        if (!Extensiones.Contains(Path.GetExtension(ruta).ToLowerInvariant()))
        {
            Error = "Usa una imagen JPG, PNG o WebP.";
            return;
        }

        var tamano = dialogo.Tamano(ruta);
        if (tamano > TamanoMaximoOriginal)
        {
            Error = "La imagen supera 10 MB.";
            return;
        }

        try
        {
            var reducida = reductor.Reducir(dialogo.Leer(ruta));
            _tamanoOriginal = tamano;
            Error = null;
            Aviso = null;
            Nueva = reducida;
        }
        catch (Exception error) when (error is ImagenNoValida or IOException or UnauthorizedAccessException)
        {
            Error = "No pudimos leer la imagen.";
        }
    }

    private bool PuedeGuardar() => Nueva is not null && AvisoAceptado && !Ocupado;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (Nueva is not { } foto)
        {
            return;
        }

        Ocupado = true;
        try
        {
            await servicio.GuardarAsync(equipoId, foto, CancellationToken.None);
            Cambiada?.Invoke(this, new CambioFoto(
                foto.Miniatura,
                foto.Vista,
                new Aviso(
                    "Fotografía guardada",
                    $"{Placa}: se guardó reducida ({Kb(foto.TamanoBytes)} KB en lugar de {Kb(_tamanoOriginal)} KB).",
                    "RF-031",
                    EsError: false)));
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, "No pudimos guardar la fotografía");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private bool PuedeQuitarFoto() => TieneFoto && !Ocupado;

    [RelayCommand(CanExecute = nameof(PuedeQuitarFoto))]
    private async Task QuitarAsync()
    {
        Ocupado = true;
        try
        {
            await servicio.QuitarAsync(equipoId, CancellationToken.None);
            Cambiada?.Invoke(this, new CambioFoto(
                null,
                null,
                new Aviso("Fotografía quitada", $"{Placa} vuelve a mostrar el ícono de su tipo.", "RF-031", EsError: false)));
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, "No pudimos quitar la fotografía");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke(this, EventArgs.Empty);

    private static Aviso AvisoDe(ErrorApi error, string titulo) => new(
        error.SinConexion ? "Sin conexión" : titulo,
        error.Message,
        error.SinConexion ? "MSG-011" : error.Codigo,
        PuedeReintentar: error.SinConexion);

    private static string Kb(long bytes) => Math.Max(1, (long)Math.Round(bytes / 1024d)).ToString("N0", TextosEquipo.Cultura);
}

/// <summary>Resultado del diálogo: la nueva miniatura y vista (null si se quitó) y la confirmación para la hoja de vida.</summary>
public sealed record CambioFoto(byte[]? Miniatura, byte[]? Vista, Aviso Confirmacion);
