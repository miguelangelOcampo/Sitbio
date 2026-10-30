using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-001 Inicio de sesión (HU-001, CU-001). La contraseña no se enlaza desde el PasswordBox:
/// la vista la copia en <see cref="Contrasena"/> y la limpia cuando el modelo lo pide.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IServicioAutenticacion _servicio;

    public LoginViewModel(IServicioAutenticacion servicio)
    {
        _servicio = servicio;
    }

    /// <summary>Se dispara al iniciar sesión; la ventana principal cambia de vista.</summary>
    public event EventHandler<SesionUsuario>? SesionIniciada;

    /// <summary>MSG-001: la vista pone el foco en «Contraseña».</summary>
    public event EventHandler? FocoEnContrasenaSolicitado;

    [ObservableProperty]
    private string _correo = string.Empty;

    [ObservableProperty]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TieneErrorCorreo))]
    private string? _errorCorreo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TieneErrorContrasena))]
    private string? _errorContrasena;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAviso))]
    private Aviso? _aviso;

    [ObservableProperty]
    private bool _mostrarContrasena;

    [ObservableProperty]
    private bool _mostrarAyudaClave;

    public bool TieneErrorCorreo => ErrorCorreo is not null;

    public bool TieneErrorContrasena => ErrorContrasena is not null;

    public bool HayAviso => Aviso is not null;

    public IReadOnlyList<PlanServicio> Planes => PlanServicio.Vigentes;

    [RelayCommand]
    private async Task IniciarSesionAsync(CancellationToken cancellationToken)
    {
        Aviso = null;
        if (!Validar())
        {
            return;
        }

        var resultado = await _servicio.IniciarSesionAsync(Correo.Trim(), Contrasena, cancellationToken);
        if (resultado.Sesion is { } sesion)
        {
            Contrasena = string.Empty;
            SesionIniciada?.Invoke(this, sesion);
            return;
        }

        switch (resultado.Fallo)
        {
            case FalloLogin.CredencialesIncorrectas:
                // DOC-05 E1: se conserva el correo, se vacía la contraseña y el foco va a ella.
                Aviso = new Aviso("No pudimos iniciar sesión", Mensajes.Msg001, "MSG-001");
                Contrasena = string.Empty;
                FocoEnContrasenaSolicitado?.Invoke(this, EventArgs.Empty);
                break;
            case FalloLogin.AccesoNoDisponible:
                Aviso = new Aviso("No fue posible iniciar sesión", Mensajes.Msg002, "MSG-002");
                Contrasena = string.Empty;
                break;
            case FalloLogin.SinConexion:
                Aviso = new Aviso("Sin conexión con el servidor", Mensajes.Msg011, "MSG-011", PuedeReintentar: true);
                break;
            case FalloLogin.DemasiadosIntentos:
                Aviso = new Aviso("Demasiados intentos", "Espere un minuto antes de intentar de nuevo.", null);
                break;
            default:
                Aviso = new Aviso("No pudimos iniciar sesión", "Ocurrió un error inesperado. Intente de nuevo en unos minutos.", null);
                break;
        }
    }

    [RelayCommand]
    private void AlternarContrasena() => MostrarContrasena = !MostrarContrasena;

    [RelayCommand]
    private void AlternarAyudaClave() => MostrarAyudaClave = !MostrarAyudaClave;

    private bool Validar()
    {
        var correo = Correo.Trim();
        var arroba = correo.IndexOf('@');
        ErrorCorreo = correo.Length == 0
            ? "Ingrese su correo institucional."
            : arroba < 1 || correo.IndexOf('.', arroba) < arroba + 2 || correo.EndsWith('.')
                ? "Escriba un correo válido, por ejemplo nombre@institucion.co."
                : null;
        ErrorContrasena = Contrasena.Length == 0 ? "Ingrese su contraseña." : null;
        return ErrorCorreo is null && ErrorContrasena is null;
    }
}
