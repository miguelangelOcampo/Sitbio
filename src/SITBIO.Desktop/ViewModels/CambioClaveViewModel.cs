using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-002 Cambio de contraseña (RF-012, HU-013): obligatorio cuando el usuario entra con una
/// clave temporal (no se puede omitir; solo cerrar sesión) o voluntario desde el marco de la
/// sesión. Las reglas se marcan mientras se escribe; la API las vuelve a validar.
/// </summary>
public partial class CambioClaveViewModel : ObservableObject
{
    private readonly IServicioSesion _servicio;
    private bool _intentoGuardar;

    public CambioClaveViewModel(IServicioSesion servicio, bool obligatorio)
    {
        _servicio = servicio;
        Obligatorio = obligatorio;
        Reglas =
        [
            new ReglaClave(ReglasClave.Longitud, c => c.Length >= ReglasClave.LongitudMinima),
            new ReglaClave(ReglasClave.MayusculaMinuscula, c => c.Any(char.IsUpper) && c.Any(char.IsLower)),
            new ReglaClave(ReglasClave.Numero, c => c.Any(char.IsDigit)),
            new ReglaClave(ReglasClave.Simbolo, c => c.Any(ch => !char.IsLetterOrDigit(ch))),

            // El cliente no conoce el nombre ni el correo (el token no los lleva): la API la revisa al guardar.
            new ReglaClave(ReglasClave.DatosPersonales, null),
        ];
    }

    /// <summary>Clave temporal: no se puede cancelar, solo cerrar sesión (FLU-001 A1).</summary>
    public bool Obligatorio { get; }

    public bool EsVoluntario => !Obligatorio;

    public string Titulo => Obligatorio ? "Crea tu nueva contraseña" : "Cambiar contraseña";

    public string EtiquetaActual => Obligatorio ? "Contraseña temporal" : "Contraseña actual";

    public IReadOnlyList<ReglaClave> Reglas { get; }

    /// <summary>La contraseña quedó guardada; quien abrió la pantalla decide a dónde ir.</summary>
    public event EventHandler? Guardada;

    /// <summary>Voluntario: «Cancelar». Obligatorio: «Cerrar sesión».</summary>
    public event EventHandler? Cancelada;

    [ObservableProperty]
    private string _actual = string.Empty;

    [ObservableProperty]
    private string _nueva = string.Empty;

    [ObservableProperty]
    private string _confirmacion = string.Empty;

    [ObservableProperty]
    private bool _mostrar;

    [ObservableProperty]
    private string? _errorActual;

    [ObservableProperty]
    private string? _errorNueva;

    [ObservableProperty]
    private string? _errorConfirmacion;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyPropertyChangedFor(nameof(TextoGuardar))]
    private bool _ocupado;

    public string TextoGuardar => Ocupado ? "Guardando…" : "Guardar contraseña";

    partial void OnActualChanged(string value) => Revalidar();

    partial void OnNuevaChanged(string value)
    {
        foreach (var regla in Reglas)
        {
            regla.Evaluar(value, _intentoGuardar);
        }

        Revalidar();
    }

    partial void OnConfirmacionChanged(string value) => Revalidar();

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        _intentoGuardar = true;
        foreach (var regla in Reglas)
        {
            regla.Evaluar(Nueva, true);
        }

        if (!Validar())
        {
            Aviso = CamposMarcados();
            return;
        }

        Ocupado = true;
        try
        {
            await _servicio.CambiarContrasenaAsync(Actual, Nueva, CancellationToken.None);
            Actual = Nueva = Confirmacion = string.Empty;
            Aviso = null;
            Guardada?.Invoke(this, EventArgs.Empty);
        }
        catch (ErrorApi error)
        {
            MostrarError(error);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private bool PuedeGuardar() => !Ocupado;

    [RelayCommand]
    private void Cancelar() => Cancelada?.Invoke(this, EventArgs.Empty);

    /// <summary>«Mostrar contraseñas»: muestra u oculta los tres campos a la vez.</summary>
    [RelayCommand]
    private void AlternarMostrar() => Mostrar = !Mostrar;

    /// <summary>Errores del cliente; después del primer intento se actualizan mientras se escribe.</summary>
    public bool Validar()
    {
        ErrorActual = Actual.Length == 0
            ? (Obligatorio ? "Escribe la contraseña temporal que recibiste." : "Escribe tu contraseña actual.")
            : null;
        ErrorNueva = Nueva.Length == 0 ? "Escribe la nueva contraseña."
            : Reglas.Any(r => r.Cumple == false) ? "No cumple todas las reglas."
            : Nueva == Actual ? "Debe ser diferente de la actual."
            : null;
        ErrorConfirmacion = Confirmacion.Length == 0 || Confirmacion != Nueva ? "Las contraseñas no coinciden." : null;
        return ErrorActual is null && ErrorNueva is null && ErrorConfirmacion is null;
    }

    private void Revalidar()
    {
        if (_intentoGuardar)
        {
            Validar();
        }
    }

    public void MostrarError(ErrorApi error)
    {
        if (error.Errores.Count > 0)
        {
            ErrorActual = error.Errores.GetValueOrDefault("actual");
            if (error.Errores.GetValueOrDefault("nueva") is { } nueva)
            {
                // La API devuelve el texto de cada regla incumplida, incluida la de datos personales.
                var incumplidas = Reglas.Where(r => nueva.Contains(r.Texto, StringComparison.Ordinal)).ToList();
                foreach (var regla in incumplidas)
                {
                    regla.MarcarIncumplida();
                }

                ErrorNueva = incumplidas.Count > 0 ? "No cumple todas las reglas." : nueva;
            }

            Aviso = CamposMarcados();
            return;
        }

        Aviso = new Aviso(
            error.SinConexion ? "Sin conexión" : "No se guardó la contraseña",
            error.Message,
            error.Codigo ?? (error.SinConexion ? "MSG-011" : null));
    }

    private static Aviso CamposMarcados() => new("Revisa los campos marcados", "Corrige la información para guardar tu nueva contraseña.", "MSG-008");
}

/// <summary>Textos de las reglas: los mismos que devuelve la API en «errors.nueva» (PoliticaContrasena).</summary>
public static class ReglasClave
{
    public const int LongitudMinima = 10;
    public const string Longitud = "Al menos 10 caracteres.";
    public const string MayusculaMinuscula = "Una letra mayúscula y una minúscula.";
    public const string Numero = "Al menos un número.";
    public const string Simbolo = "Al menos un símbolo (por ejemplo * # $ !).";
    public const string DatosPersonales = "No contiene tu nombre, tu apellido ni tu correo.";
}

/// <summary>
/// Una regla de la contraseña con su estado: cumple (verde), no cumple (rojo tras intentar
/// guardar) o pendiente (gris). La de datos personales solo la confirma la API.
/// </summary>
public partial class ReglaClave(string texto, Func<string, bool>? prueba) : ObservableObject
{
    public string Texto { get; } = texto;

    /// <summary>True o false; null mientras no se pueda saber (sin texto o revisada solo por la API).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estado))]
    private bool? _cumple;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estado))]
    private bool _resaltarIncumplida;

    /// <summary>«Cumple», «Incumple» o «Pendiente»: el estilo del punto y el texto.</summary>
    public string Estado => Cumple == true ? "Cumple" : Cumple == false && ResaltarIncumplida ? "Incumple" : "Pendiente";

    public void Evaluar(string clave, bool intentoGuardar)
    {
        Cumple = prueba is null ? null : clave.Length > 0 && prueba(clave);
        ResaltarIncumplida = intentoGuardar;
    }

    /// <summary>La API la rechazó (por ejemplo, la de datos personales).</summary>
    public void MarcarIncumplida()
    {
        Cumple = false;
        ResaltarIncumplida = true;
    }
}
