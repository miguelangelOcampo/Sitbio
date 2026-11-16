using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Un plan tal como se muestra en «Conoce los planes».</summary>
public sealed record PlanVisible(string Nombre, string Capacidad, string Valor, string Periodo)
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public static PlanVisible Desde(PlanOfrecido plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var capacidad = plan.MaxUsuariosGestion is { } usuarios && plan.MaxEquipos is { } equipos && plan.MaxAlmacenamientoGb is { } gb
            ? string.Create(Colombia, $"{usuarios} usuarios de gestión · {equipos:N0} equipos · {gb} GB")
            : "Capacidad a la medida";
        return plan.ValorMensualCop is { } valor
            ? new PlanVisible(plan.Nombre, capacidad, string.Create(Colombia, $"${valor:N0}"), "COP/mes")
            : new PlanVisible(plan.Nombre, capacidad, "Cotización", string.Empty);
    }
}

/// <summary>
/// «Conoce los planes» desde UI-001 (HU-028, RF-029): consulta los planes vigentes y envía al proveedor
/// una solicitud de demostración o cotización con los datos de la institución. Al enviarla muestra el
/// código de la solicitud (MSG-021, CA-HU-028-01).
/// </summary>
public partial class SolicitudServicioViewModel : ObservableObject
{
    public const string TipoDemostracion = "demostracion";
    public const string TipoCotizacion = "cotizacion";
    public const string SinPlan = "Aún no lo sé";

    private readonly IServicioSolicitudesServicio _servicio;

    public SolicitudServicioViewModel(IServicioSolicitudesServicio servicio)
    {
        _servicio = servicio;
    }

    /// <summary>La vista de inicio de sesión cierra el panel.</summary>
    public event EventHandler? CierreSolicitado;

    public System.Collections.ObjectModel.ObservableCollection<PlanVisible> Planes { get; } = [];

    /// <summary>Opciones de plan para la solicitud: los planes vigentes más «Aún no lo sé».</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> OpcionesPlan { get; } = [SinPlan];

    [ObservableProperty]
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

    /// <summary>MSG-021: código de la solicitud enviada; con él se muestra la confirmación.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Enviada))]
    private string? _codigoEnviado;

    public bool Enviada => CodigoEnviado is not null;

    public string TipoSolicitado => EsCotizacion ? TipoCotizacion : TipoDemostracion;

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
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso("No pudimos cargar los planes", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: error.SinConexion);
        }
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
        }
        catch (ErrorApi error)
        {
            UbicarError(error);
        }
    }

    [RelayCommand]
    private void Cerrar() => CierreSolicitado?.Invoke(this, EventArgs.Empty);

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
