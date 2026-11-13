using System.Globalization;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Ordenes;

/// <summary>
/// Aprobación y firma de la orden por el Ingeniero Biomédico (HU-021; RF-021, RN-004, RN-007; API-OT-APROB).
/// Solo se aprueba una orden «cerrada» por el técnico. Primero se marca la intervención como firmada en
/// MongoDB (<c>cerrada_firmada</c>, estado de salida y copia del equipo); luego, en una transacción de
/// PostgreSQL, la orden pasa a «aprobada» con la firma, el tiquete se cierra y el equipo toma su estado
/// de salida. Si MongoDB falla, nada cambia; si falla PostgreSQL, repetir la aprobación vuelve a marcar el
/// mismo documento y termina el cambio.
/// </summary>
public sealed class GestionAprobacion(
    IRepositorioOrdenes ordenes,
    IRepositorioIntervenciones intervenciones,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    /// <summary>CA-HU-021-02: aprueba con la firma escrita por el Ingeniero y deja la orden inalterable.</summary>
    public async Task<OrdenDto> AprobarAsync(Guid id, DatosAprobacion datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var firma = datos.Firma?.Trim() ?? string.Empty;
        if (firma.Length == 0)
        {
            throw ErrorValidacion.DeCampo("firma", "Escribe tu firma (nombre completo) para aprobar la orden.");
        }

        if (firma.Length > OrdenTrabajo.LongitudMaximaFirma)
        {
            throw ErrorValidacion.DeCampo("firma", $"La firma admite máximo {OrdenTrabajo.LongitudMaximaFirma} caracteres.");
        }

        var orden = await ordenes.OrdenAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("La orden de trabajo");
        switch (orden.Estado)
        {
            case EstadosOrden.Aprobada:
                throw new ErrorConflicto(GestionEjecucion.CodigoFirmada, "Esta orden ya está aprobada y firmada; su registro no se puede modificar (RN-004).");
            case not EstadosOrden.Cerrada:
                throw new ErrorConflicto(GestionEjecucion.CodigoTransicionInvalida, $"Solo se aprueba una orden cerrada por el técnico; esta está «{orden.Estado}».");
        }

        var guardada = await intervenciones.ObtenerAsync(orden.Id, cancellationToken)
            ?? throw new InvalidOperationException("La orden cerrada no tiene intervención.");
        var salida = guardada.Intervencion.EstadoEquipoSalida
            ?? throw new InvalidOperationException("La intervención de la orden cerrada no tiene estado de salida.");
        var equipo = await ordenes.EquipoAsync(orden.EquipoId, cancellationToken)
            ?? throw new InvalidOperationException("El equipo de la orden no se encontró.");
        ReglaCalibracion.Validar(equipo, salida, FechaColombia.Hoy(tiempo));

        var ahora = tiempo.GetUtcNow();
        var foto = new FotoEquipo(
            $"{equipo.TipoEquipo} {equipo.Equipo.PlacaInventario}",
            equipo.Equipo.Marca,
            equipo.Equipo.Modelo,
            equipo.TipoEquipo,
            equipo.Ubicacion,
            ahora);

        // MongoDB primero: si no responde, la orden sigue «cerrada» y se puede intentar de nuevo.
        await intervenciones.FirmarAsync(guardada.ObjectId, salida, foto, cancellationToken);

        orden.Aprobar(contexto.UsuarioId, firma, ahora);
        HistorialTiquete? cambioTiquete = null;
        if (orden.TiqueteId is { } tiqueteId)
        {
            var tiquete = await ordenes.TiqueteAsync(tiqueteId, cancellationToken);
            if (tiquete is not null && tiquete.Estado != EstadosTiquete.Cerrado)
            {
                cambioTiquete = tiquete.Cerrar(contexto.UsuarioId, "Orden de trabajo aprobada y firmada por el Ingeniero.", ahora);
            }
        }

        HistorialEstadoEquipo? cambioEquipo = null;
        var estadoSalida = EstadosSalidaEquipo.IdEstadoEquipo(salida);
        if (!equipo.Equipo.DadoDeBaja && equipo.Equipo.EstadoEquipoId != estadoSalida)
        {
            cambioEquipo = equipo.Equipo.CambiarEstado(estadoSalida, $"Orden de trabajo aprobada: el equipo sale «{salida}».", contexto.UsuarioId, ahora);
        }

        await ordenes.GuardarAprobacionAsync(cambioTiquete, cambioEquipo, cancellationToken);
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.CierreOt,
            GestionOrdenes.Entidad,
            orden.Id.ToString(),
            new Dictionary<string, string>
            {
                ["intervencion"] = guardada.ObjectId,
                ["estado_salida"] = salida,
                ["tiquete_cerrado"] = cambioTiquete is null ? "no" : "si",
            },
            cancellationToken);

        var leida = await ordenes.ObtenerAsync(orden.Id, null, cancellationToken)
            ?? throw new InvalidOperationException("La orden recién aprobada no se pudo leer.");
        return OrdenDto.Desde(leida);
    }
}

/// <summary>
/// RN-007 (con RN-010): un equipo que requiere calibración y la tiene vencida no sale «Operativo» de una
/// orden hasta registrar una nueva calibración. Un equipo que no requiere calibración no se revisa.
/// </summary>
public static class ReglaCalibracion
{
    public const string Codigo = "RN-007";

    public static void Validar(EquipoDeLaOrden equipo, string? estadoSalida, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        if (estadoSalida == EstadosSalidaEquipo.Operativo
            && equipo.Equipo.RequiereCalibracion
            && equipo.VenceCalibracion is { } vence
            && vence < hoy)
        {
            // MSG-013 (DOC-05).
            throw new ErrorConflicto(
                Codigo,
                $"La calibración de este equipo venció el {vence.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}. Registre una nueva calibración antes de cerrar la orden como «Operativo».");
        }
    }
}

/// <summary>Cuerpo de POST /ordenes/{id}/aprobacion: la firma electrónica interna que escribe el Ingeniero.</summary>
public sealed record DatosAprobacion(string? Firma);

/// <summary>Equipo de la orden (con seguimiento de cambios) con su tipo, ubicación y el vencimiento de su última calibración.</summary>
public sealed record EquipoDeLaOrden(EquipoBiomedico Equipo, string TipoEquipo, string Ubicacion, DateOnly? VenceCalibracion);
