using System.Globalization;
using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Peticiones;

/// <summary>Valores de <c>tiquete.criticidad</c> (script 01, CHECK; RF-018).</summary>
public static class Criticidades
{
    public const string Critica = "critica";
    public const string Alta = "alta";
    public const string Media = "media";
    public const string Baja = "baja";

    public static IReadOnlyList<string> Todas { get; } = [Critica, Alta, Media, Baja];

    public static bool EsValida(string? criticidad) => criticidad is not null && Todas.Contains(criticidad);
}

/// <summary>Valores de <c>tiquete.estado</c> (script 01, CHECK; RF-017).</summary>
public static class EstadosTiquete
{
    public const string Recibido = "recibido";
    public const string Asignado = "asignado";
    public const string EnEjecucion = "en_ejecucion";
    public const string Cerrado = "cerrado";

    public static IReadOnlyList<string> Todos { get; } = [Recibido, Asignado, EnEjecucion, Cerrado];
}

/// <summary>
/// Reporte de falla de un equipo (tabla <c>tiquete</c>, script 01; HU-003, HU-018). Nace «recibido»
/// y recibe su código TQT-###### al guardarse: el consecutivo es por institución
/// (<c>uq_tiquete_tenant_codigo</c>) y lo asigna la persistencia dentro de la transacción.
/// </summary>
public sealed class Tiquete : EntidadTenant
{
    public const string Prefijo = "TQT-";
    /// <summary>DOC-05, UI-009: descripción opcional de máximo 500 caracteres.</summary>
    public const int LongitudMaximaDescripcion = 500;
    public const int ConsecutivoMaximo = 999_999;

    // Constructor para EF Core.
    private Tiquete(Guid tenantId)
        : base(tenantId)
    {
    }

    public Tiquete(
        Guid tenantId,
        Guid equipoId,
        Guid reportanteId,
        string tipoFalla,
        string? descripcion,
        string criticidad,
        DateTimeOffset fechaReporte)
        : base(tenantId)
    {
        EquipoId = equipoId != Guid.Empty ? equipoId : throw new ArgumentException("Selecciona el equipo que presenta la falla.", nameof(equipoId));
        ReportanteId = reportanteId != Guid.Empty ? reportanteId : throw new ArgumentException("El tiquete debe tener quién lo reporta.", nameof(reportanteId));
        TipoFalla = Texto.Requerido(tipoFalla, nameof(tipoFalla), Inventario.TipoFalla.LongitudMaximaNombre);
        Descripcion = Texto.Opcional(descripcion, LongitudMaximaDescripcion, nameof(descripcion)) ?? string.Empty;
        Criticidad = Criticidades.EsValida(criticidad)
            ? criticidad
            : throw new ArgumentException("La criticidad debe ser crítica, alta, media o baja.", nameof(criticidad));
        Estado = EstadosTiquete.Recibido;
        FechaReporte = fechaReporte;
    }

    public Guid EquipoId { get; private set; }

    /// <summary>Quién reporta (sale del token, nunca del cuerpo de la petición).</summary>
    public Guid ReportanteId { get; private set; }

    /// <summary>Vacío hasta que la persistencia lo numera.</summary>
    public string Codigo { get; private set; } = string.Empty;

    /// <summary>Nombre del tipo de falla elegido; es texto para que renombrar el catálogo no cambie tiquetes anteriores (V009).</summary>
    public string TipoFalla { get; private set; } = string.Empty;

    /// <summary>Opcional (DOC-05); vacía si no se escribió, porque la columna es NOT NULL.</summary>
    public string Descripcion { get; private set; } = string.Empty;

    public string Criticidad { get; private set; } = string.Empty;

    public string Estado { get; private set; } = string.Empty;

    public DateTimeOffset FechaReporte { get; private set; }

    public DateTimeOffset? FechaCierre { get; private set; }

    /// <summary>RN-005: una falla crítica deja el equipo «Fuera de servicio» hasta el cierre de la orden.</summary>
    public bool EsCritica => Criticidad == Criticidades.Critica;

    /// <summary>CA-HU-018-01: la prioridad sugerida para la orden de trabajo es la criticidad reportada.</summary>
    public string PrioridadSugerida => Criticidad;

    /// <summary>Asigna el código TQT-###### una sola vez.</summary>
    public void Numerar(int consecutivo)
    {
        if (Codigo.Length > 0)
        {
            throw new InvalidOperationException("El tiquete ya tiene código.");
        }

        Codigo = Formatear(consecutivo);
    }

    /// <summary>Primer registro del historial: el tiquete queda «recibido» a nombre de quien reporta.</summary>
    public HistorialTiquete Recepcion() =>
        new(TenantId, Id, ReportanteId, null, EstadosTiquete.Recibido, "Falla reportada.", FechaReporte);

    /// <summary>
    /// HU-004: el tiquete pasa de «recibido» a «asignado» cuando se crea su orden de trabajo. Devuelve el
    /// registro de <c>historial_tiquete</c> a nombre de quien asigna. Solo un tiquete recibido se asigna.
    /// </summary>
    public HistorialTiquete Asignar(Guid usuarioId, string comentario, DateTimeOffset fecha)
    {
        if (Estado != EstadosTiquete.Recibido)
        {
            throw new InvalidOperationException($"El tiquete {Codigo} ya no está «recibido»; no se puede asignar de nuevo.");
        }

        Estado = EstadosTiquete.Asignado;
        return new HistorialTiquete(TenantId, Id, usuarioId, EstadosTiquete.Recibido, EstadosTiquete.Asignado, comentario, fecha);
    }

    /// <summary>
    /// HU-005: el tiquete pasa de «asignado» a «en ejecución» la primera vez que el técnico inicia su
    /// orden. Devuelve el registro de <c>historial_tiquete</c> a nombre del técnico.
    /// </summary>
    public HistorialTiquete IniciarEjecucion(Guid usuarioId, string comentario, DateTimeOffset fecha)
    {
        if (Estado != EstadosTiquete.Asignado)
        {
            throw new InvalidOperationException($"El tiquete {Codigo} no está «asignado»; no puede pasar a «en ejecución».");
        }

        Estado = EstadosTiquete.EnEjecucion;
        return new HistorialTiquete(TenantId, Id, usuarioId, EstadosTiquete.Asignado, EstadosTiquete.EnEjecucion, comentario, fecha);
    }

    /// <summary>
    /// HU-021: el tiquete se cierra cuando el Ingeniero aprueba su orden de trabajo; guarda la fecha de
    /// cierre y devuelve el registro de <c>historial_tiquete</c> a nombre de quien aprueba.
    /// </summary>
    public HistorialTiquete Cerrar(Guid usuarioId, string comentario, DateTimeOffset fecha)
    {
        if (Estado is not (EstadosTiquete.Asignado or EstadosTiquete.EnEjecucion))
        {
            throw new InvalidOperationException($"El tiquete {Codigo} está «{Estado}»; solo se cierra un tiquete asignado o en ejecución.");
        }

        var anterior = Estado;
        Estado = EstadosTiquete.Cerrado;
        FechaCierre = fecha;
        return new HistorialTiquete(TenantId, Id, usuarioId, anterior, EstadosTiquete.Cerrado, comentario, fecha);
    }

    public static string Formatear(int consecutivo) =>
        consecutivo is >= 1 and <= ConsecutivoMaximo
            ? Prefijo + consecutivo.ToString("D6", CultureInfo.InvariantCulture)
            : throw new ArgumentOutOfRangeException(nameof(consecutivo), "El consecutivo de tiquetes va de 1 a 999999.");

    /// <summary>Número del código (TQT-000042 → 42); null si no tiene el formato.</summary>
    public static int? Consecutivo(string? codigo) =>
        codigo is { Length: 10 } && codigo.StartsWith(Prefijo, StringComparison.Ordinal)
            && int.TryParse(codigo.AsSpan(Prefijo.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            ? numero
            : null;
}

/// <summary>
/// Cambio de estado o comentario de un tiquete (tabla <c>historial_tiquete</c>, script 01; RF-017).
/// Solo se inserta. El primer registro (recepción) no tiene estado anterior.
/// </summary>
public sealed class HistorialTiquete : EntidadTenant
{
    public const int LongitudMaximaComentario = 500;

    // Constructor para EF Core.
    private HistorialTiquete(Guid tenantId)
        : base(tenantId)
    {
    }

    internal HistorialTiquete(
        Guid tenantId,
        Guid tiqueteId,
        Guid usuarioId,
        string? estadoAnterior,
        string nuevoEstado,
        string? comentario,
        DateTimeOffset fechaCambio)
        : base(tenantId)
    {
        TiqueteId = tiqueteId;
        UsuarioId = usuarioId;
        EstadoAnterior = estadoAnterior;
        NuevoEstado = nuevoEstado;
        Comentario = Texto.Opcional(comentario, LongitudMaximaComentario, nameof(comentario));
        FechaCambio = fechaCambio;
    }

    public Guid TiqueteId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string? EstadoAnterior { get; private set; }

    public string NuevoEstado { get; private set; } = string.Empty;

    public string? Comentario { get; private set; }

    public DateTimeOffset FechaCambio { get; private set; }
}
