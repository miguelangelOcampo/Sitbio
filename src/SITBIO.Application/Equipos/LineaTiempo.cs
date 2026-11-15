using System.Globalization;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Equipos;

/// <summary>Tipos de evento de la línea de tiempo (CMP-UI-014: una plantilla por tipo).</summary>
public static class TiposEvento
{
    public const string Registro = "registro";
    public const string Estado = "estado";
    public const string Tiquete = "tiquete";
    public const string Orden = "orden";
    public const string Intervencion = "intervencion";
    public const string Calibracion = "calibracion";
}

/// <summary>Evento de la línea de tiempo del equipo (API-EQ-HV).</summary>
public sealed record EventoLineaTiempoDto(DateTimeOffset Fecha, string Tipo, string Titulo, string? Detalle, string? Responsable, Guid? ReferenciaId);

/// <summary>
/// Respuesta de GET /equipos/{id}/linea-tiempo: los eventos del más antiguo al más reciente.
/// <see cref="IntervencionesCompletas"/> es false si MongoDB no respondió y faltan las intervenciones.
/// </summary>
public sealed record LineaTiempoDto(Guid EquipoId, string PlacaInventario, IReadOnlyList<EventoLineaTiempoDto> Eventos, bool IntervencionesCompletas);

/// <summary>Cambio de estado con los nombres de los estados y de quién lo hizo.</summary>
public sealed record EstadoLeido(HistorialEstadoEquipo Cambio, string? EstadoAnterior, string NuevoEstado, string Usuario);

/// <summary>Orden con los nombres del técnico y de quien la aprobó.</summary>
public sealed record OrdenDelEquipo(OrdenTrabajo Orden, string Tecnico, string? AprobadoPor, string? CodigoTiquete);

/// <summary>Calibración con el nombre de quien la registró.</summary>
public sealed record CalibracionDelEquipo(RegistroCalibracion Calibracion, string RegistradoPor);

/// <summary>Lo que PostgreSQL sabe del equipo para su línea de tiempo.</summary>
public sealed record HistoriaEquipo(
    Guid EquipoId,
    string PlacaInventario,
    DateTimeOffset FechaRegistro,
    IReadOnlyList<EstadoLeido> Estados,
    IReadOnlyList<Tiquete> Tiquetes,
    IReadOnlyList<OrdenDelEquipo> Ordenes,
    IReadOnlyList<CalibracionDelEquipo> Calibraciones);

/// <summary>Historia del equipo en PostgreSQL; el filtro de tenant lo aplica la persistencia (RN-008).</summary>
public interface IRepositorioLineaTiempo
{
    /// <summary>null si el equipo no existe o es de otra institución.</summary>
    Task<HistoriaEquipo?> HistoriaAsync(Guid equipoId, CancellationToken cancellationToken);
}

/// <summary>
/// Línea de tiempo del equipo (HU-023, RF-023; CA-HU-023-01): une el registro, los cambios de estado, los
/// tiquetes, las órdenes, las intervenciones (MongoDB) y las calibraciones en orden cronológico. Es solo de
/// consulta (CA-HU-023-02): la ven AC-01 a AC-05 y AC-07; el permiso lo exige la API. Si MongoDB no responde,
/// devuelve lo de PostgreSQL y lo avisa.
/// </summary>
public sealed class ConsultaLineaTiempo(IRepositorioLineaTiempo repositorio, IRepositorioIntervenciones intervenciones)
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public async Task<LineaTiempoDto> ConsultarAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        var historia = await repositorio.HistoriaAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var eventos = new List<EventoLineaTiempoDto>
        {
            new(historia.FechaRegistro, TiposEvento.Registro, "Equipo registrado en el inventario", $"Placa {historia.PlacaInventario}", null, null),
        };

        eventos.AddRange(historia.Estados.Select(e => new EventoLineaTiempoDto(
            e.Cambio.FechaCambio,
            TiposEvento.Estado,
            e.EstadoAnterior is null ? $"Estado: {e.NuevoEstado}" : $"Estado: {e.EstadoAnterior} → {e.NuevoEstado}",
            e.Cambio.Motivo,
            e.Usuario,
            null)));

        foreach (var t in historia.Tiquetes)
        {
            eventos.Add(new(t.FechaReporte, TiposEvento.Tiquete, $"Falla reportada · {t.Codigo}", $"{t.TipoFalla} · criticidad {t.Criticidad}", null, t.Id));
            if (t.FechaCierre is { } cierre)
            {
                eventos.Add(new(cierre, TiposEvento.Tiquete, $"Tiquete {t.Codigo} cerrado", null, null, t.Id));
            }
        }

        foreach (var leida in historia.Ordenes)
        {
            var o = leida.Orden;
            var tipo = o.TipoMantenimiento == TiposMantenimiento.Preventivo ? "preventiva" : "correctiva";
            var origen = leida.CodigoTiquete is { } codigo ? $"Del tiquete {codigo}" : o.PlanId is not null ? "Generada por el plan preventivo" : null;
            eventos.Add(new(o.FechaAsignacion ?? o.FechaCreacion, TiposEvento.Orden, $"Orden {tipo} asignada", origen, leida.Tecnico, o.Id));
            if (o.FechaCierre is { } cierre)
            {
                eventos.Add(new(cierre, TiposEvento.Orden, $"Orden {tipo} cerrada", null, leida.Tecnico, o.Id));
            }

            if (o.FechaAprobacion is { } aprobacion)
            {
                eventos.Add(new(aprobacion, TiposEvento.Orden, $"Orden {tipo} aprobada y firmada", o.FirmaAprobacion, leida.AprobadoPor, o.Id));
            }
        }

        eventos.AddRange(historia.Calibraciones.Select(c => new EventoLineaTiempoDto(
            new DateTimeOffset(c.Calibracion.FechaCalibracion.ToDateTime(TimeOnly.MinValue), FechaColombia.Desfase),
            TiposEvento.Calibracion,
            $"Calibración {c.Calibracion.Resultado}",
            $"Patrón {c.Calibracion.PatronReferencia} · vence el {c.Calibracion.FechaVencimiento.ToString("dd/MM/yyyy", Cultura)}",
            c.RegistradoPor,
            c.Calibracion.Id)));

        var completas = true;
        try
        {
            var tecnicos = historia.Ordenes.ToDictionary(o => o.Orden.Id, o => o.Tecnico);
            eventos.AddRange((await intervenciones.DelEquipoAsync(equipoId, cancellationToken)).Select(i => new EventoLineaTiempoDto(
                i.FechaIntervencion,
                TiposEvento.Intervencion,
                i.EstadoEquipoSalida is { } salida ? $"Intervención registrada · sale «{salida}»" : "Intervención registrada",
                $"{i.Diagnostico} — {i.AccionesRealizadas}",
                tecnicos.GetValueOrDefault(i.OrdenId),
                i.OrdenId)));
        }
        catch (ErrorServicioNoDisponible)
        {
            completas = false;
        }

        var ordenados = eventos.OrderBy(e => e.Fecha).ThenBy(e => Peso(e.Tipo)).ToList();
        return new LineaTiempoDto(historia.EquipoId, historia.PlacaInventario, ordenados, completas);
    }

    /// <summary>Con la misma fecha, el orden natural del ciclo: registro, falla, orden, intervención, calibración, estado.</summary>
    private static int Peso(string tipo) => tipo switch
    {
        TiposEvento.Registro => 0,
        TiposEvento.Tiquete => 1,
        TiposEvento.Orden => 2,
        TiposEvento.Intervencion => 3,
        TiposEvento.Calibracion => 4,
        _ => 5,
    };
}
