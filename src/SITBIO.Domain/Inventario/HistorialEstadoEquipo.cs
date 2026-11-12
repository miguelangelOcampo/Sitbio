using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Un cambio de estado de un equipo con su motivo, fecha y usuario (tabla <c>historial_estado_equipo</c>,
/// script 01; RF-014, CA-HU-014-01). Solo se inserta: el historial no se modifica ni se borra.
/// El primer registro (alta) no tiene estado anterior.
/// </summary>
public sealed class HistorialEstadoEquipo : EntidadTenant
{
    public const int LongitudMaximaMotivo = 300;

    // Constructor para EF Core.
    private HistorialEstadoEquipo(Guid tenantId)
        : base(tenantId)
    {
    }

    internal HistorialEstadoEquipo(
        Guid tenantId,
        Guid equipoId,
        short? estadoAnteriorId,
        short nuevoEstadoId,
        Guid usuarioId,
        string? motivo,
        DateTimeOffset fechaCambio)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("El cambio debe ser de un equipo.", nameof(equipoId));
        UsuarioId = usuarioId != Guid.Empty ? usuarioId : throw new ArgumentException("El cambio debe tener un usuario responsable.", nameof(usuarioId));
        EstadoAnteriorId = estadoAnteriorId;
        NuevoEstadoId = nuevoEstadoId;
        Motivo = motivo;
        FechaCambio = fechaCambio;
    }

    public Guid EquipoId { get; private set; }

    /// <summary>Null en el registro del alta.</summary>
    public short? EstadoAnteriorId { get; private set; }

    public short NuevoEstadoId { get; private set; }

    /// <summary>Quién hizo el cambio (sale del token, nunca del cuerpo de la petición).</summary>
    public Guid UsuarioId { get; private set; }

    public string? Motivo { get; private set; }

    public DateTimeOffset FechaCambio { get; private set; }

    /// <summary>Primer registro del historial: el equipo nace en su estado inicial, sin estado anterior.</summary>
    public static HistorialEstadoEquipo Alta(EquipoBiomedico equipo, Guid usuarioId)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        return new HistorialEstadoEquipo(equipo.TenantId, equipo.Id, null, equipo.EstadoEquipoId, usuarioId, null, equipo.FechaCreacion);
    }
}
