using System.Globalization;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;

namespace Sitbio.Application.Mantenimiento;

/// <summary>
/// Alertas de vencimiento (HU-020, RF-020; API-ALERTAS) y generación de las órdenes preventivas (HU-019,
/// CA-HU-019-02). Al consultar, primero se generan las órdenes de los planes cuya próxima fecha llegó, cada
/// una para el técnico activo menos ocupado; después se listan los preventivos y las calibraciones que
/// vencen dentro de los días de anticipación de la institución (HU-024), los vencidos primero. Consultan
/// AC-02 y AC-03 (la API lo exige).
/// </summary>
public sealed class GestionAlertas(
    IRepositorioPlanes repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public async Task<AlertasDto> ConsultarAsync(CancellationToken cancellationToken)
    {
        var hoy = FechaColombia.Hoy(tiempo);
        var generadas = await GenerarOrdenesAsync(hoy, cancellationToken);

        var dias = await repositorio.DiasAnticipacionAsync(cancellationToken);
        var limite = hoy.AddDays(dias);
        var planes = await repositorio.PlanesPorVencerAsync(limite, cancellationToken);
        var calibraciones = await repositorio.CalibracionesPorVencerAsync(limite, cancellationToken);

        var alertas = planes
            .Select(p => Alerta(
                TiposAlerta.Preventivo,
                p.Leido.Plan.ProximaFecha,
                hoy,
                p.Leido.Plan.EquipoId,
                p.Leido.PlacaInventario,
                p.Leido.TipoEquipo,
                p.Leido.Ubicacion,
                p.Leido.Plan.Descripcion,
                p.Leido.Plan.Id,
                p.OrdenId,
                p.EstadoOrden))
            .Concat(calibraciones.Select(c => Alerta(
                TiposAlerta.Calibracion,
                c.FechaVencimiento,
                hoy,
                c.EquipoId,
                c.PlacaInventario,
                c.TipoEquipo,
                c.Ubicacion,
                "Calibración",
                null,
                null,
                null)))
            .OrderBy(a => a.Estado == EstadosAlerta.Vencida ? 0 : 1)
            .ThenBy(a => a.Fecha)
            .ThenBy(a => a.PlacaInventario, StringComparer.Ordinal)
            .ToList();
        return new AlertasDto(dias, alertas, generadas);
    }

    /// <summary>
    /// Reparte los planes (del más atrasado al más reciente) entre los técnicos activos, siempre al menos
    /// ocupado. Sin técnicos activos no se genera nada: la alerta sigue visible hasta que haya uno.
    /// </summary>
    public static IReadOnlyList<OrdenTrabajo> Repartir(IReadOnlyList<PlanPreventivo> planes, IReadOnlyList<TecnicoConCarga> tecnicos, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(planes);
        ArgumentNullException.ThrowIfNull(tecnicos);
        if (tecnicos.Count == 0)
        {
            return [];
        }

        var carga = tecnicos.Select((t, i) => (t.Id, Carga: t.OrdenesAbiertas, Orden: i)).ToList();
        var ordenes = new List<OrdenTrabajo>();
        foreach (var plan in planes.OrderBy(p => p.ProximaFecha).ThenBy(p => p.FechaCreacion).ThenBy(p => p.Id))
        {
            var elegido = carga.OrderBy(t => t.Carga).ThenBy(t => t.Orden).First();
            ordenes.Add(OrdenTrabajo.Preventiva(plan, elegido.Id, ahora));
            carga[carga.IndexOf(elegido)] = elegido with { Carga = elegido.Carga + 1 };
        }

        return ordenes;
    }

    private async Task<int> GenerarOrdenesAsync(DateOnly hoy, CancellationToken cancellationToken)
    {
        var ahora = tiempo.GetUtcNow();
        var ordenes = await repositorio.GenerarOrdenesAsync(hoy, (planes, tecnicos) => Repartir(planes, tecnicos, ahora), cancellationToken);
        foreach (var orden in ordenes)
        {
            await auditoria.RegistrarAsync(
                contexto.UsuarioId,
                AccionesAuditoria.Crear,
                GestionOrdenes.Entidad,
                orden.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["plan"] = orden.PlanId!.Value.ToString(),
                    ["tipo"] = orden.TipoMantenimiento,
                    ["fechaProgramada"] = orden.FechaProgramada!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["origen"] = "generada por el plan preventivo",
                },
                cancellationToken);
        }

        return ordenes.Count;
    }

    private static AlertaDto Alerta(
        string tipo,
        DateOnly fecha,
        DateOnly hoy,
        Guid equipoId,
        string placa,
        string tipoEquipo,
        string ubicacion,
        string descripcion,
        Guid? planId,
        Guid? ordenId,
        string? estadoOrden) =>
        new(
            tipo,
            fecha < hoy ? EstadosAlerta.Vencida : EstadosAlerta.Proxima,
            fecha,
            fecha.DayNumber - hoy.DayNumber,
            equipoId,
            placa,
            tipoEquipo,
            ubicacion,
            descripcion,
            planId,
            ordenId,
            estadoOrden);
}
