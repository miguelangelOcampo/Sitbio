using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Services;

/// <summary>
/// Control de la sesión en el cliente (HU-012, RF-011, SEC-002): cierra la sesión tras el
/// tiempo de inactividad (MSG-003), renueva el token mientras el usuario sigue activo y la
/// termina si la API rechaza el token. La ventana llama a <see cref="RegistrarActividad"/>
/// con cada tecla o clic y a <see cref="RevisarAsync"/> cada <see cref="IntervaloRevision"/>.
/// </summary>
public sealed class ControlSesion
{
    public const string VariableMinutosInactividad = "SITBIO_MINUTOS_INACTIVIDAD";
    public const int MinutosInactividad = 30;

    public static readonly TimeSpan IntervaloRevision = TimeSpan.FromSeconds(30);

    /// <summary>Margen antes del vencimiento del token para renovarlo.</summary>
    public static readonly TimeSpan RenovarAntesDe = TimeSpan.FromMinutes(5);

    private readonly IServicioSesion _servicio;
    private readonly SesionActual _sesion;
    private readonly TimeProvider _tiempo;
    private DateTimeOffset _ultimaActividad;
    private int _cerrando;

    public ControlSesion(IServicioSesion servicio, SesionActual sesion, TimeProvider tiempo, TimeSpan? limiteInactividad = null)
    {
        _servicio = servicio;
        _sesion = sesion;
        _tiempo = tiempo;
        LimiteInactividad = limiteInactividad ?? LimiteDesdeEntorno();
        _sesion.Rechazada += (_, _) => Terminar(CierreSesion.Rechazo);
    }

    public TimeSpan LimiteInactividad { get; }

    /// <summary>Se dispara (desde cualquier hilo) cuando la sesión termina sin que el usuario la cierre.</summary>
    public event EventHandler<CierreSesion>? SesionTerminada;

    /// <summary>Empieza a contar desde el inicio de sesión.</summary>
    public void Iniciar()
    {
        _ultimaActividad = _tiempo.GetUtcNow();
        Interlocked.Exchange(ref _cerrando, 0);
    }

    public void RegistrarActividad() => _ultimaActividad = _tiempo.GetUtcNow();

    public async Task RevisarAsync()
    {
        if (_sesion.Sesion is not { } actual || Volatile.Read(ref _cerrando) == 1)
        {
            return;
        }

        var ahora = _tiempo.GetUtcNow();
        if (ahora - _ultimaActividad >= LimiteInactividad)
        {
            // CA-HU-012-01: se revoca el token en la API antes de olvidarlo.
            if (Interlocked.Exchange(ref _cerrando, 1) == 0)
            {
                await _servicio.CerrarAsync(ServicioSesionHttp.MotivoInactividad, CancellationToken.None);
                _sesion.Cerrar();
                SesionTerminada?.Invoke(this, CierreSesion.Inactividad);
            }

            return;
        }

        // Activo y con el token por vencer: se renueva (DOC-06, «renovar mientras haya actividad»).
        if (actual.ExpiraEn - ahora <= RenovarAntesDe)
        {
            var renovacion = await _servicio.RenovarAsync(CancellationToken.None);
            if (renovacion.Sesion is { } nueva)
            {
                _sesion.Iniciar(nueva);
            }
            else if (renovacion.Rechazada)
            {
                Terminar(CierreSesion.Rechazo);
            }
        }
    }

    /// <summary>Cierre voluntario (CA-HU-012-02): revoca el token y lo olvida aunque la API no responda.</summary>
    public async Task CerrarVoluntarioAsync()
    {
        Interlocked.Exchange(ref _cerrando, 1);
        await _servicio.CerrarAsync(ServicioSesionHttp.MotivoVoluntario, CancellationToken.None);
        _sesion.Cerrar();
    }

    /// <summary>
    /// Límite de inactividad: 30 minutos (RF-011). La variable de entorno solo sirve para
    /// probarlo con menos tiempo (de 1 a 30 minutos); nunca lo alarga.
    /// </summary>
    public static TimeSpan LimiteDesdeEntorno()
    {
        var valor = Environment.GetEnvironmentVariable(VariableMinutosInactividad);
        return int.TryParse(valor, out var minutos) && minutos is >= 1 and <= MinutosInactividad
            ? TimeSpan.FromMinutes(minutos)
            : TimeSpan.FromMinutes(MinutosInactividad);
    }

    private void Terminar(CierreSesion cierre)
    {
        if (Interlocked.Exchange(ref _cerrando, 1) == 0)
        {
            _sesion.Cerrar();
            SesionTerminada?.Invoke(this, cierre);
        }
    }
}

/// <summary>Aviso que se muestra al terminar la sesión sin que el usuario la cierre.</summary>
public sealed record CierreSesion(string Titulo, string Mensaje, string? Codigo)
{
    public static CierreSesion Inactividad { get; } = new("Tu sesión expiró", Mensajes.Msg003, "MSG-003");

    public static CierreSesion Rechazo { get; } = new("Tu sesión terminó", "Por seguridad la sesión ya no es válida. Inicia sesión de nuevo para continuar.", null);
}
