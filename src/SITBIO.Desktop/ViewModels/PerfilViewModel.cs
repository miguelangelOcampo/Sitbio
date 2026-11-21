using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// «Mi perfil» y «Preferencias» (prototipo <c>screens/Perfil.tsx</c>), desde el pie del panel. Los datos de la cuenta
/// son de solo lectura: los gestiona el Administrador (UI-004). El tema se aplica al instante y se guarda en la
/// estación (<c>%LOCALAPPDATA%\SITBIO\preferencias.json</c>). J-12: «Cambiar foto» y «Quitar foto»; la foto se reduce en el
/// cliente a 128 y 512 px antes de subirla y se ve al instante en el avatar.
/// </summary>
public partial class PerfilViewModel : ObservableObject
{
    public const string IdPantalla = "PERFIL";

    private readonly SesionActual _sesion;
    private readonly IServicioPreferencias _preferencias;
    private readonly ITemaAplicacion _tema;
    private readonly IServicioFotoPerfil? _fotos;
    private readonly IDialogoArchivos? _dialogo;
    private readonly IReductorFoto? _reductor;
    private string _temaActual;

    public PerfilViewModel(
        SesionActual sesion,
        IServicioPreferencias preferencias,
        ITemaAplicacion tema,
        FotoPerfilActual? foto = null,
        IServicioFotoPerfil? fotos = null,
        IDialogoArchivos? dialogo = null,
        IReductorFoto? reductor = null)
    {
        _sesion = sesion;
        _preferencias = preferencias;
        _tema = tema;
        _temaActual = preferencias.Leer().Tema;
        Foto = foto ?? new FotoPerfilActual();
        _fotos = fotos;
        _dialogo = dialogo;
        _reductor = reductor;
        Foto.PropertyChanged += (_, _) => QuitarFotoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>J-12: la misma foto que muestra el avatar de la barra superior.</summary>
    public FotoPerfilActual Foto { get; }

    /// <summary>Se puede cambiar la foto si hay conexión con la API (siempre en la aplicación).</summary>
    public bool PuedeCambiarFoto => _fotos is not null && _dialogo is not null && _reductor is not null;

    /// <summary>Resultado de cambiar o quitar la foto: confirmación o error.</summary>
    [ObservableProperty]
    private Aviso? _avisoFoto;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CambiarFotoCommand), nameof(QuitarFotoCommand))]
    private bool _guardandoFoto;

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

    // ---------- Foto de perfil (J-12) ----------

    /// <summary>Tamaño máximo del archivo original; se reduce antes de subirlo.</summary>
    public const long TamanoMaximoOriginal = FotoEquipoViewModel.TamanoMaximoOriginal;

    private bool PuedeCambiar() => PuedeCambiarFoto && !GuardandoFoto;

    [RelayCommand(CanExecute = nameof(PuedeCambiar))]
    private async Task CambiarFotoAsync()
    {
        var ruta = _dialogo!.ElegirArchivo("Elige tu foto de perfil", FotoEquipoViewModel.FiltroImagenes);
        if (ruta is null)
        {
            return;
        }

        if (!FotoEquipoViewModel.Extensiones.Contains(Path.GetExtension(ruta).ToLowerInvariant()))
        {
            AvisoFoto = Error("Usa una imagen JPG, PNG o WebP.");
            return;
        }

        if (_dialogo.Tamano(ruta) > TamanoMaximoOriginal)
        {
            AvisoFoto = Error("La imagen supera 10 MB.");
            return;
        }

        FotoReducida reducida;
        try
        {
            reducida = _reductor!.Reducir(_dialogo.Leer(ruta));
        }
        catch (Exception error) when (error is ImagenNoValida or IOException or UnauthorizedAccessException)
        {
            AvisoFoto = Error("No pudimos leer la imagen.");
            return;
        }

        GuardandoFoto = true;
        try
        {
            await _fotos!.GuardarAsync(reducida, CancellationToken.None);
            Foto.Miniatura = reducida.Miniatura;
            AvisoFoto = new Aviso("Foto actualizada", "Tu foto ya se ve en el menú de la cuenta.", null, EsError: false);
        }
        catch (ErrorApi error)
        {
            AvisoFoto = Error(error.Message, error.SinConexion ? "Sin conexión" : "No pudimos guardar tu foto", error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            GuardandoFoto = false;
        }
    }

    private bool PuedeQuitar() => PuedeCambiarFoto && Foto.TieneFoto && !GuardandoFoto;

    [RelayCommand(CanExecute = nameof(PuedeQuitar))]
    private async Task QuitarFotoAsync()
    {
        GuardandoFoto = true;
        try
        {
            await _fotos!.QuitarAsync(CancellationToken.None);
            Foto.Limpiar();
            AvisoFoto = new Aviso("Foto quitada", "Vuelven a verse tus iniciales.", null, EsError: false);
        }
        catch (ErrorApi error)
        {
            AvisoFoto = Error(error.Message, error.SinConexion ? "Sin conexión" : "No pudimos quitar tu foto", error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            GuardandoFoto = false;
        }
    }

    [RelayCommand]
    private void CerrarAvisoFoto() => AvisoFoto = null;

    private static Aviso Error(string mensaje, string titulo = "Revisa la imagen", string? codigo = null) => new(titulo, mensaje, codigo);
}
