using System.Threading.Channels;
using Sitbio.Application.Instituciones;

namespace Sitbio.Api.Modules.Instituciones;

/// <summary>Cola en memoria de los avisos por correo de las solicitudes guardadas (J-05).</summary>
public sealed class ColaAvisosSolicitudes : IAvisoSolicitudes
{
    private readonly Channel<AvisoSolicitud> _canal = Channel.CreateUnbounded<AvisoSolicitud>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<AvisoSolicitud> Pendientes => _canal.Reader;

    public void Encolar(AvisoSolicitud aviso)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        _canal.Writer.TryWrite(aviso);
    }
}

/// <summary>
/// Envía al proveedor el correo de cada solicitud del servicio (J-05, RF-029): toma los avisos de la cola, los arma
/// con <see cref="PlantillaAvisoSolicitud"/> y los manda por SMTP con un reintento. Si el envío falla, la solicitud ya
/// quedó guardada y el registro solo lleva el código y el tipo de error, sin datos personales. Sin configuración de
/// correo no intenta enviar.
/// </summary>
public sealed class ServicioAvisosSolicitudes(
    ColaAvisosSolicitudes cola,
    IEnviadorCorreo enviador,
    OpcionesCorreo opciones,
    ILogger<ServicioAvisosSolicitudes> registro) : BackgroundService
{
    public const int Intentos = 2;

    /// <summary>Espera antes del reintento.</summary>
    public TimeSpan EsperaReintento { get; init; } = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var aviso in cola.Pendientes.ReadAllAsync(stoppingToken))
        {
            await EnviarAsync(aviso, stoppingToken);
        }
    }

    /// <summary>Envía un aviso con un reintento. true si salió.</summary>
    public async Task<bool> EnviarAsync(AvisoSolicitud aviso, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        if (!opciones.EstaConfigurado)
        {
            registro.LogWarning("El aviso de la solicitud {Codigo} no se envió: falta la configuración del correo", aviso.Codigo);
            return false;
        }

        var mensaje = PlantillaAvisoSolicitud.Crear(aviso, opciones.Destino!);
        for (var intento = 1; intento <= Intentos; intento++)
        {
            try
            {
                await enviador.EnviarAsync(mensaje, cancellationToken);
                registro.LogInformation("Aviso de la solicitud {Codigo} enviado al proveedor", aviso.Codigo);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                // Sin el mensaje de la excepción: el servidor SMTP puede incluir direcciones de correo.
                registro.LogError(
                    "Falló el intento {Intento} de {Intentos} de avisar la solicitud {Codigo}: {Error}",
                    intento, Intentos, aviso.Codigo, error.GetType().Name);
                if (intento < Intentos)
                {
                    await Task.Delay(EsperaReintento, cancellationToken);
                }
            }
        }

        return false;
    }
}
