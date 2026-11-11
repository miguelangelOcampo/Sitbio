using CommunityToolkit.Mvvm.ComponentModel;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Diálogo de la hoja de vida (UI-006) para dar de baja un equipo o revertir su baja (HU-014,
/// RF-014; prototipo HojaVida.tsx). Solo arma y valida el motivo; el envío lo hace
/// <see cref="DetalleEquipoViewModel"/>. La fecha la pone el servidor.
/// </summary>
public partial class BajaEquipoViewModel : ObservableObject
{
    /// <summary>Longitud máxima del motivo en historial_estado_equipo (la valida también la API).</summary>
    public const int LongitudMaxima = 300;

    /// <summary>Mínimo de la descripción con «Otro» y del motivo de la reversión.</summary>
    public const int LongitudMinima = 10;

    public const string Otro = "Otro";

    public static readonly IReadOnlyList<string> Motivos =
        ["Obsolescencia tecnológica", "Daño irreparable", "Fin de vida útil", "Traslado o donación", Otro];

    public BajaEquipoViewModel(string placa, string tipo, bool esReversion)
    {
        EsReversion = esReversion;
        Placa = placa;
        Titulo = esReversion ? $"Revertir la baja de {placa}" : $"Dar de baja {tipo} {placa}";
        Opciones = esReversion ? [] : Motivos.Select(m => new OpcionMotivo(m, this)).ToList();
    }

    public bool EsReversion { get; }

    public bool EsBaja => !EsReversion;

    public string Placa { get; }

    public string Titulo { get; }

    public string TextoConfirmar => EsReversion ? "Revertir baja" : "Dar de baja";

    public string EtiquetaObservacion => EsReversion
        ? "Motivo de la reversión"
        : MotivoElegido == Otro ? "Describe el motivo" : "Observación (opcional)";

    public IReadOnlyList<OpcionMotivo> Opciones { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EtiquetaObservacion))]
    private string? _motivoElegido;

    [ObservableProperty]
    private string _observacion = string.Empty;

    [ObservableProperty]
    private string? _errorMotivo;

    [ObservableProperty]
    private string? _errorObservacion;

    /// <summary>MSG-008 al validar o el error de la API (EQ-001, EQ-002, RN-009, sin conexión).</summary>
    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private bool _enviando;

    partial void OnMotivoElegidoChanged(string? value)
    {
        ErrorMotivo = null;
        foreach (var opcion in Opciones)
        {
            opcion.Notificar();
        }
    }

    partial void OnObservacionChanged(string value) => ErrorObservacion = null;

    /// <summary>Motivo que se guarda: «Motivo · observación» en la baja; el texto escrito en la reversión.</summary>
    public string Motivo
    {
        get
        {
            var observacion = Observacion.Trim();
            if (EsReversion || MotivoElegido == Otro)
            {
                return observacion;
            }

            return observacion.Length == 0 ? MotivoElegido ?? string.Empty : $"{MotivoElegido} · {observacion}";
        }
    }

    /// <summary>Marca los campos y deja MSG-008 si falta algo; true si se puede enviar.</summary>
    public bool Validar()
    {
        var observacion = Observacion.Trim();
        ErrorMotivo = EsBaja && MotivoElegido is null ? "Elige el motivo de la baja." : null;
        var pideTexto = EsReversion || MotivoElegido == Otro;
        ErrorObservacion = pideTexto && observacion.Length < LongitudMinima
            ? $"Escribe el motivo (al menos {LongitudMinima} caracteres)."
            : Motivo.Length > LongitudMaxima
                ? $"El motivo admite hasta {LongitudMaxima} caracteres."
                : null;

        var valido = ErrorMotivo is null && ErrorObservacion is null;
        Aviso = valido ? null : new Aviso("Revisa los campos marcados", "Completa el motivo para continuar.", "MSG-008");
        return valido;
    }

    public void MostrarError(ErrorApi error)
    {
        if (error.Errores.TryGetValue("motivo", out var motivo))
        {
            ErrorObservacion = motivo;
        }

        Aviso = new Aviso(
            error.SinConexion ? "Sin conexión" : EsReversion ? "No se pudo revertir la baja" : "No se pudo dar de baja el equipo",
            error.Message,
            error.SinConexion ? "MSG-011" : error.Codigo);
    }
}

/// <summary>Opción de motivo como botón de radio; la selección vive en el diálogo.</summary>
public sealed class OpcionMotivo(string texto, BajaEquipoViewModel dialogo) : ObservableObject
{
    public string Texto { get; } = texto;

    public bool Elegida
    {
        get => dialogo.MotivoElegido == Texto;
        set
        {
            if (value)
            {
                dialogo.MotivoElegido = Texto;
            }
        }
    }

    internal void Notificar() => OnPropertyChanged(nameof(Elegida));
}
