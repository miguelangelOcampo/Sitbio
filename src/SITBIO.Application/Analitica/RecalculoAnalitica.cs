using System.Collections.Concurrent;
using System.Threading.Channels;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Analitica;

/// <summary>Estados del recálculo de la analítica de una institución.</summary>
public static class EstadosRecalculo
{
    /// <summary>Nadie ha pedido un recálculo desde que inició la API.</summary>
    public const string SinSolicitud = "sin_solicitud";
    public const string EnCurso = "en_curso";
    public const string Terminado = "terminado";
    public const string Fallido = "fallido";
}

/// <summary>
/// Respuesta de POST y GET /analitica/agrupamiento (HU-008): el estado del último recálculo pedido y la fecha del
/// último resultado guardado por el lote (null si aún no hay ninguno).
/// </summary>
public sealed record EstadoRecalculoDto(
    string Estado,
    DateTimeOffset? IniciadoEn,
    DateTimeOffset? TerminadoEn,
    string? Mensaje,
    DateTimeOffset? UltimoCalculo);

/// <summary>Ejecuta el lote de analítica (ADR-006) solo para una institución; lanza una excepción si falla.</summary>
public interface IEjecutorAnalitica
{
    Task EjecutarAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Cola y estado de los recálculos, compartidos por toda la API (singleton). Un recálculo por institución a la vez;
/// el servicio en segundo plano toma las solicitudes de la cola y las ejecuta en orden.
/// </summary>
public sealed class CoordinadorRecalculos(TimeProvider reloj)
{
    public const string MensajeFallo = "No pudimos recalcular la analítica. Intenta de nuevo; si sigue fallando, avisa al administrador.";

    private readonly Channel<Guid> _cola = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, Registro> _estados = new();

    public ChannelReader<Guid> Pendientes => _cola.Reader;

    /// <summary>Pone el recálculo en la cola; false si la institución ya tiene uno en curso.</summary>
    public bool Encolar(Guid tenantId)
    {
        var nuevo = new Registro(EstadosRecalculo.EnCurso, reloj.GetUtcNow(), null, null);
        var actual = _estados.AddOrUpdate(
            tenantId,
            nuevo,
            (_, anterior) => anterior.Estado == EstadosRecalculo.EnCurso ? anterior : nuevo);
        if (!ReferenceEquals(actual, nuevo))
        {
            return false;
        }

        _cola.Writer.TryWrite(tenantId);
        return true;
    }

    public void Terminar(Guid tenantId) => Cerrar(tenantId, EstadosRecalculo.Terminado, null);

    public void Fallar(Guid tenantId) => Cerrar(tenantId, EstadosRecalculo.Fallido, MensajeFallo);

    public (string Estado, DateTimeOffset? IniciadoEn, DateTimeOffset? TerminadoEn, string? Mensaje) Estado(Guid tenantId) =>
        _estados.TryGetValue(tenantId, out var r)
            ? (r.Estado, r.IniciadoEn, r.TerminadoEn, r.Mensaje)
            : (EstadosRecalculo.SinSolicitud, null, null, null);

    private void Cerrar(Guid tenantId, string estado, string? mensaje) =>
        _estados.AddOrUpdate(
            tenantId,
            _ => new Registro(estado, null, reloj.GetUtcNow(), mensaje),
            (_, anterior) => anterior with { Estado = estado, TerminadoEn = reloj.GetUtcNow(), Mensaje = mensaje });

    private sealed record Registro(string Estado, DateTimeOffset? IniciadoEn, DateTimeOffset? TerminadoEn, string? Mensaje);
}

/// <summary>
/// Recálculo de la analítica bajo demanda (I-04, HU-008): AC-02 lo pide para su institución y la API responde
/// enseguida; el lote corre en segundo plano y GET informa el estado y la fecha del último cálculo.
/// </summary>
public sealed class RecalculoAnalitica(
    CoordinadorRecalculos coordinador,
    IRepositorioAnalitica repositorio,
    IContextoUsuario contexto,
    IRegistroAuditoria auditoria)
{
    public const string CodigoEnCurso = "ANA-002";
    public const string Entidad = "resultados_analitica";

    public async Task<EstadoRecalculoDto> SolicitarAsync(CancellationToken cancellationToken)
    {
        if (!coordinador.Encolar(contexto.TenantId))
        {
            throw new ErrorConflicto(CodigoEnCurso, "Ya se está recalculando la analítica de tu institución. Espera a que termine.");
        }

        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Actualizar,
            Entidad,
            contexto.TenantId.ToString(),
            new Dictionary<string, string> { ["operacion"] = "recalcular" },
            cancellationToken);
        return await EstadoAsync(cancellationToken);
    }

    public async Task<EstadoRecalculoDto> EstadoAsync(CancellationToken cancellationToken)
    {
        var (estado, iniciado, terminado, mensaje) = coordinador.Estado(contexto.TenantId);
        var ultimo = await repositorio.UltimoAsync(cancellationToken);
        return new EstadoRecalculoDto(estado, iniciado, terminado, mensaje, ultimo?.GeneradoEn);
    }
}
