using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Tarjeta de un plan en «Planes de SITBIO» (J-04, como el prototipo): a quién va dirigido, el valor de referencia y
/// lo que incluye. Sin límites de capacidad el plan es «a la medida» y se pide por cotización.
/// </summary>
public sealed partial class PlanVisible : ObservableObject
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public PlanVisible(string nombre, string dirigido, string valor, string periodo, IReadOnlyList<string> incluye, bool aLaMedida)
    {
        Nombre = nombre;
        Dirigido = dirigido;
        Valor = valor;
        Periodo = periodo;
        Incluye = incluye;
        ALaMedida = aLaMedida;
    }

    public string Nombre { get; }

    public string Dirigido { get; }

    public string Valor { get; }

    public string Periodo { get; }

    /// <summary>Capacidad y servicio del plan, cada uno con su marca de verificación.</summary>
    public IReadOnlyList<string> Incluye { get; }

    public bool ALaMedida { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBoton))]
    private bool _seleccionado;

    public string TextoBoton => Seleccionado ? "Seleccionado" : ALaMedida ? "Pedir cotización" : "Elegir este plan";

    public static PlanVisible Desde(PlanOfrecido plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var medida = plan.MaxUsuariosGestion is null && plan.MaxEquipos is null && plan.MaxAlmacenamientoGb is null;
        var incluye = new List<string>
        {
            plan.MaxUsuariosGestion is { } usuarios ? string.Create(Colombia, $"Hasta {usuarios:N0} usuarios de gestión") : "Usuarios de gestión a la medida",
            plan.MaxEquipos is { } equipos ? string.Create(Colombia, $"Hasta {equipos:N0} equipos activos") : "Equipos activos a la medida",
            plan.MaxAlmacenamientoGb is { } gb ? string.Create(Colombia, $"{gb:N0} GB de documentos") : "Almacenamiento a la medida",
            "Personal clínico ilimitado",
            "Soporte " + Soporte(plan.Nombre),
        };
        var (valor, periodo) = plan.ValorMensualCop is { } cop ? (string.Create(Colombia, $"${cop:N0}"), "COP/mes") : ("Cotización", string.Empty);
        return new PlanVisible(plan.Nombre, DirigidoA(plan.Nombre), valor, periodo, incluye, medida || plan.ValorMensualCop is null);
    }

    private static string DirigidoA(string nombre) => nombre switch
    {
        "Consultorio" => "Consultorios y baja complejidad",
        "Clínica" => "Clínicas e IPS pequeñas",
        "Hospital" => "Hospitales de mediana complejidad",
        "Red de Salud" => "Redes y grupos de instituciones",
        _ => "Instituciones de salud",
    };

    private static string Soporte(string nombre) => nombre switch
    {
        "Hospital" => "prioritario",
        "Red de Salud" => "prioritario con acompañamiento",
        _ => "en horario hábil",
    };
}

/// <summary>Paso del diálogo «Planes de SITBIO».</summary>
public enum PasoSolicitud
{
    Planes,
    Formulario,
    Enviada,
}

/// <summary>
/// «Planes de SITBIO» desde UI-001 (HU-028, RF-029; J-04 como el prototipo): compara los planes vigentes en tarjetas y
/// envía al proveedor una solicitud de demostración o cotización con los datos de la institución. Al elegir un plan se
/// abre el formulario con ese plan marcado. Al enviarla muestra el código de la solicitud (MSG-021, CA-HU-028-01).
/// </summary>
public partial class SolicitudServicioViewModel : ObservableObject
{
    public const string TipoDemostracion = "demostracion";
    public const string TipoCotizacion = "cotizacion";
    public const string SinPlan = "Aún no lo sé";
    public const string Demostracion = "Demostración";
    public const string Cotizacion = "Cotización";

    private readonly IServicioSolicitudesServicio _servicio;

    public SolicitudServicioViewModel(IServicioSolicitudesServicio servicio)
    {
        _servicio = servicio;
    }

    /// <summary>La vista de inicio de sesión cierra el diálogo.</summary>
    public event EventHandler? CierreSolicitado;

    public ObservableCollection<PlanVisible> Planes { get; } = [];

    /// <summary>Opciones de plan para la solicitud: «Aún no lo sé» más los planes vigentes.</summary>
    public ObservableCollection<string> OpcionesPlan { get; } = [SinPlan];

    public static IReadOnlyList<string> OpcionesTipo { get; } = [Demostracion, Cotizacion];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerPlanes), nameof(VerFormulario), nameof(Enviada), nameof(Titulo))]
    private PasoSolicitud _paso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TipoElegido), nameof(TipoSolicitado))]
    private bool _esCotizacion;

    [ObservableProperty]
    private string _institucion = string.Empty;

    [ObservableProperty]
    private string _nit = string.Empty;

    [ObservableProperty]
    private string _contacto = string.Empty;

    [ObservableProperty]
    private string _correo = string.Empty;

    [ObservableProperty]
    private string _telefono = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanResumen))]
    private string _plan = SinPlan;

    [ObservableProperty]
    private string _mensaje = string.Empty;

    [ObservableProperty]
    private string? _errorInstitucion;

    [ObservableProperty]
    private string? _errorNit;

    [ObservableProperty]
    private string? _errorContacto;

    [ObservableProperty]
    private string? _errorCorreo;

    [ObservableProperty]
    private string? _errorTelefono;

    [ObservableProperty]
    private string? _errorMensaje;

    [ObservableProperty]
    private Aviso? _aviso;

    /// <summary>MSG-021: código de la solicitud enviada.</summary>
    [ObservableProperty]
    private string? _codigoEnviado;

    public bool VerPlanes => Paso == PasoSolicitud.Planes;

    public bool VerFormulario => Paso == PasoSolicitud.Formulario;

    public bool Enviada => Paso == PasoSolicitud.Enviada;

    public string Titulo => VerFormulario ? "Solicitar el servicio" : "Planes de SITBIO";

    public string TipoSolicitado => EsCotizacion ? TipoCotizacion : TipoDemostracion;

    /// <summary>El tipo de solicitud como lo elige el usuario en el formulario.</summary>
    public string TipoElegido
    {
        get => EsCotizacion ? Cotizacion : Demostracion;
        set => EsCotizacion = value == Cotizacion;
    }

    /// <summary>Plan de interés en la confirmación.</summary>
    public string PlanResumen => Plan == SinPlan ? "Por definir" : Plan;

    /// <summary>Correo al que responderá SITBIO, para la confirmación.</summary>
    public string CorreoEnviado => Correo.Trim();

    [RelayCommand]
    private async Task CargarAsync(CancellationToken cancellationToken)
    {
        try
        {
            var planes = await _servicio.PlanesAsync(cancellationToken);
            Planes.Clear();
            OpcionesPlan.Clear();
            OpcionesPlan.Add(SinPlan);
            foreach (var plan in planes)
            {
                Planes.Add(PlanVisible.Desde(plan));
                OpcionesPlan.Add(plan.Nombre);
            }

            MarcarSeleccion();
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso("No pudimos cargar los planes", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: error.SinConexion);
        }
    }

    /// <summary>«Elegir este plan» o «Pedir cotización»: abre el formulario con el plan marcado.</summary>
    [RelayCommand]
    private void ElegirPlan(PlanVisible? plan)
    {
        if (plan is null)
        {
            return;
        }

        Plan = plan.Nombre;
        if (plan.ALaMedida)
        {
            EsCotizacion = true;
        }

        Paso = PasoSolicitud.Formulario;
    }

    /// <summary>«Solicitar demostración o cotización» sin elegir plan.</summary>
    [RelayCommand]
    private void IrAFormulario() => Paso = PasoSolicitud.Formulario;

    [RelayCommand]
    private void VolverAPlanes()
    {
        Aviso = null;
        Paso = PasoSolicitud.Planes;
    }

    [RelayCommand]
    private async Task EnviarAsync(CancellationToken cancellationToken)
    {
        Aviso = null;
        if (!Validar())
        {
            return;
        }

        var datos = new DatosSolicitud(
            TipoSolicitado,
            Institucion.Trim(),
            Nit.Trim(),
            Contacto.Trim(),
            Correo.Trim(),
            Vacio(Telefono),
            Plan == SinPlan ? null : Plan,
            Vacio(Mensaje));
        try
        {
            var enviada = await _servicio.EnviarAsync(datos, cancellationToken);
            CodigoEnviado = enviada.Codigo;
            OnPropertyChanged(nameof(CorreoEnviado));
            Paso = PasoSolicitud.Enviada;
        }
        catch (ErrorApi error)
        {
            UbicarError(error);
        }
    }

    [RelayCommand]
    private void Cerrar() => CierreSolicitado?.Invoke(this, EventArgs.Empty);

    partial void OnPlanChanged(string value) => MarcarSeleccion();

    private void MarcarSeleccion()
    {
        foreach (var plan in Planes)
        {
            plan.Seleccionado = plan.Nombre == Plan;
        }
    }

    private void UbicarError(ErrorApi error)
    {
        var ubicado = false;
        foreach (var (campo, mensaje) in error.Errores)
        {
            ubicado = true;
            switch (campo.ToLowerInvariant())
            {
                case "institucion": ErrorInstitucion = mensaje; break;
                case "nit": ErrorNit = mensaje; break;
                case "contacto": ErrorContacto = mensaje; break;
                case "correo": ErrorCorreo = mensaje; break;
                case "telefono": ErrorTelefono = mensaje; break;
                case "mensaje": ErrorMensaje = mensaje; break;
                default: ubicado = false; break;
            }
        }

        if (!ubicado)
        {
            Aviso = new Aviso("No pudimos enviar la solicitud", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: error.SinConexion);
        }
    }

    private bool Validar()
    {
        var institucion = Institucion.Trim();
        ErrorInstitucion = institucion.Length == 0
            ? "Escribe el nombre de la institución."
            : institucion.Length < 4 ? "El nombre de la institución debe tener al menos 4 caracteres." : null;
        ErrorNit = Nit.Trim().Length == 0
            ? "Escribe el NIT de la institución."
            : PatronNit().IsMatch(Nit.Trim()) ? null : "Usa el formato 900.000.000-0.";
        ErrorContacto = Contacto.Trim().Length == 0 ? "Escribe el nombre de la persona de contacto." : null;
        var correo = Correo.Trim();
        var arroba = correo.IndexOf('@');
        ErrorCorreo = correo.Length == 0
            ? "Escribe el correo de contacto."
            : arroba < 1 || correo.IndexOf('.', arroba) < arroba + 2 || correo.EndsWith('.')
                ? "Escribe un correo válido, por ejemplo nombre@institucion.co."
                : null;
        ErrorTelefono = Telefono.Trim().Length > 30 ? "El teléfono admite máximo 30 caracteres." : null;
        ErrorMensaje = Mensaje.Trim().Length > 1000 ? "El mensaje admite máximo 1000 caracteres." : null;
        return ErrorInstitucion is null && ErrorNit is null && ErrorContacto is null && ErrorCorreo is null && ErrorTelefono is null && ErrorMensaje is null;
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    [GeneratedRegex(@"^\d{3}\.\d{3}\.\d{3}-\d$")]
    private static partial Regex PatronNit();
}
