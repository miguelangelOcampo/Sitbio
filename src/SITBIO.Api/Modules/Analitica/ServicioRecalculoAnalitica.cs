using Sitbio.Application.Analitica;

namespace Sitbio.Api.Modules.Analitica;

/// <summary>
/// Toma de la cola los recálculos pedidos con POST /analitica/agrupamiento y ejecuta el lote de cada institución,
/// uno a la vez (I-04, HU-008). Un fallo queda en el estado del tenant y en el registro; no detiene el servicio.
/// </summary>
public sealed class ServicioRecalculoAnalitica(
    CoordinadorRecalculos coordinador,
    IServiceScopeFactory fabrica,
    ILogger<ServicioRecalculoAnalitica> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var tenant in coordinador.Pendientes.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var alcance = fabrica.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<IEjecutorAnalitica>().EjecutarAsync(tenant, stoppingToken);
                coordinador.Terminar(tenant);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                coordinador.Fallar(tenant);
                return;
            }
            catch (Exception error)
            {
                registro.LogError(error, "Falló el recálculo de la analítica del tenant {Tenant}", tenant);
                coordinador.Fallar(tenant);
            }
        }
    }
}
