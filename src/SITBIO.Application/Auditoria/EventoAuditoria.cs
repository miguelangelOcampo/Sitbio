namespace Sitbio.Application.Auditoria;

/// <summary>
/// Evento de la bitácora de auditoría (RF-013, RNF-015, HU-010): quién, qué, cuándo y desde
/// dónde. El tenant no viaja aquí: lo pone el repositorio a partir del token (RN-008).
/// El detalle nunca lleva contraseñas, tokens ni datos personales (Ley 1581 de 2012).
/// </summary>
public sealed record EventoAuditoria(
    Guid Id,
    Guid UsuarioId,
    string Accion,
    string Entidad,
    string? EntidadId,
    DateTimeOffset FechaHora,
    string? IpOrigen,
    IReadOnlyDictionary<string, string>? Detalle)
{
    public static EventoAuditoria Nuevo(
        Guid usuarioId,
        string accion,
        string entidad,
        DateTimeOffset fechaHora,
        string? entidadId = null,
        string? ipOrigen = null,
        IReadOnlyDictionary<string, string>? detalle = null)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El evento debe identificar al usuario.", nameof(usuarioId));
        }

        if (!AccionesAuditoria.Todas.Contains(accion))
        {
            throw new ArgumentException($"Acción de auditoría desconocida: {accion}.", nameof(accion));
        }

        if (string.IsNullOrWhiteSpace(entidad))
        {
            throw new ArgumentException("La entidad es obligatoria.", nameof(entidad));
        }

        return new EventoAuditoria(Guid.NewGuid(), usuarioId, accion, entidad, entidadId, fechaHora, ipOrigen, detalle);
    }
}

/// <summary>
/// Acciones que acepta el validador de <c>bitacora_auditoria</c> (script 02 + V007).
/// Agregar una acción exige una migración V### del validador.
/// </summary>
public static class AccionesAuditoria
{
    public const string LoginOk = "LOGIN_OK";
    public const string LoginFallido = "LOGIN_FALLIDO";
    public const string Crear = "CREAR";
    public const string Actualizar = "ACTUALIZAR";
    public const string Eliminar = "ELIMINAR";
    public const string Exportar = "EXPORTAR";
    public const string CierreOt = "CIERRE_OT";
    public const string CambioRol = "CAMBIO_ROL";
    public const string CambioTenant = "CAMBIO_TENANT";
    public const string ImportarCsv = "IMPORTAR_CSV";
    public const string AccesoDenegado = "ACCESO_DENEGADO";
    public const string CierreSesion = "CIERRE_SESION";

    public static IReadOnlySet<string> Todas { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LoginOk, LoginFallido, Crear, Actualizar, Eliminar, Exportar, CierreOt, CambioRol, CambioTenant, ImportarCsv, AccesoDenegado, CierreSesion,
    };
}
