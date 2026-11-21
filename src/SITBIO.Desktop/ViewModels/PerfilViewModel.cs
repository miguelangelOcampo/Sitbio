using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// «Mi perfil» y «Preferencias» (prototipo <c>screens/Perfil.tsx</c>), desde el pie del panel. Los datos de la cuenta
/// son de solo lectura: los gestiona el Administrador (UI-004). El tema se aplica al instante y se guarda en la
/// estación (<c>%LOCALAPPDATA%\SITBIO\preferencias.json</c>).
/// </summary>
public partial class PerfilViewModel : ObservableObject
{
    public const string IdPantalla = "PERFIL";

    private readonly SesionActual _sesion;
    private readonly IServicioPreferencias _preferencias;
    private readonly ITemaAplicacion _tema;
    private string _temaActual;

    public PerfilViewModel(SesionActual sesion, IServicioPreferencias preferencias, ITemaAplicacion tema)
    {
        _sesion = sesion;
        _preferencias = preferencias;
        _tema = tema;
        _temaActual = preferencias.Leer().Tema;
    }

    /// <summary>«Cambiar contraseña»: el marco abre UI-002 en modo voluntario (HU-013).</summary>
    public event EventHandler? CambioClaveSolicitado;

    private SesionUsuario? Sesion => _sesion.Sesion;

    public string NombreCompleto
    {
        get
        {
            var nombre = $"{Sesion?.Nombres} {Sesion?.Apellidos}".Trim();
            return nombre.Length > 0 ? nombre : "Usuario de SITBIO";
        }
    }

    /// <summary>Iniciales del nombre y del primer apellido para el avatar.</summary>
    public string Iniciales
    {
        get
        {
            var nombre = Sesion?.Nombres.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var apellido = Sesion?.Apellidos.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var iniciales = $"{nombre?[..1]}{apellido?[..1]}".ToUpperInvariant();
            return iniciales.Length > 0 ? iniciales : "?";
        }
    }

    public string Correo => string.IsNullOrEmpty(Sesion?.Correo) ? "—" : Sesion.Correo;

    public string CodigoRol => Sesion?.Rol ?? string.Empty;

    public string NombreRol => GuiaRoles.NombreDe(CodigoRol);

    public string Institucion => string.IsNullOrEmpty(Sesion?.Institucion) ? "—" : Sesion.Institucion;

    // ---------- Pestañas ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnPreferencias), nameof(Titulo), nameof(Subtitulo))]
    private bool _enPerfil = true;

    public bool EnPreferencias
    {
        get => !EnPerfil;
        set => EnPerfil = !value;
    }

    public string Titulo => EnPerfil ? "Mi perfil" : "Preferencias";

    public string Subtitulo => EnPerfil
        ? "Tus datos de cuenta y la seguridad de acceso."
        : "Ajustes de tu cuenta en este equipo. No afectan a otros usuarios.";

    /// <summary>Abre la pestaña pedida desde el menú del perfil.</summary>
    public void Mostrar(bool preferencias) => EnPreferencias = preferencias;

    // ---------- Tema ----------

    /// <summary>J-10: el tema de Windows, que es el valor por defecto.</summary>
    public bool EsSistema
    {
        get => _temaActual == Temas.Sistema;
        set
        {
            if (value)
            {
                CambiarTema(Temas.Sistema);
            }
        }
    }

    public bool EsClaro
    {
        get => _temaActual == Temas.Claro;
        set
        {
            if (value)
            {
                CambiarTema(Temas.Claro);
            }
        }
    }

    public bool EsOscuro
    {
        get => _temaActual == Temas.Oscuro;
        set
        {
            if (value)
            {
                CambiarTema(Temas.Oscuro);
            }
        }
    }

    private void CambiarTema(string tema)
    {
        if (tema == _temaActual)
        {
            return;
        }

        _temaActual = tema;
        _tema.Aplicar(tema);
        _preferencias.Guardar(_preferencias.Leer() with { Tema = tema });
        OnPropertyChanged(nameof(EsSistema));
        OnPropertyChanged(nameof(EsClaro));
        OnPropertyChanged(nameof(EsOscuro));
    }

    // ---------- Imagen de los equipos (RF-031) ----------

    /// <summary>«Fotografía»: la lista y la hoja de vida muestran la foto del equipo cuando tiene una.</summary>
    public bool VerFotosEquipo
    {
        get => _preferencias.Leer().VerFotosEquipo;
        set
        {
            if (value)
            {
                CambiarImagenEquipo(verFotos: true);
            }
        }
    }

    /// <summary>«Ícono del tipo»: siempre el ícono, aunque el equipo tenga foto.</summary>
    public bool VerIconosEquipo
    {
        get => !_preferencias.Leer().VerFotosEquipo;
        set
        {
            if (value)
            {
                CambiarImagenEquipo(verFotos: false);
            }
        }
    }

    private void CambiarImagenEquipo(bool verFotos)
    {
        var actuales = _preferencias.Leer();
        if (actuales.VerFotosEquipo == verFotos)
        {
            return;
        }

        _preferencias.Guardar(actuales with { VerFotosEquipo = verFotos });
        OnPropertyChanged(nameof(VerFotosEquipo));
        OnPropertyChanged(nameof(VerIconosEquipo));
    }

    [RelayCommand]
    private void CambiarClave() => CambioClaveSolicitado?.Invoke(this, EventArgs.Empty);
}
