namespace Sitbio.Application.Comun.Errores;

/// <summary>
/// Sin permiso para la acción o servicio de la institución no activo (403, RNF-003).
/// <see cref="Codigo"/> indica el mensaje del catálogo que muestra el cliente (por ejemplo MSG-004).
/// </summary>
public sealed class ErrorPermiso(string mensaje = "No tiene permiso para realizar esta acción.", string? codigo = null)
    : ErrorAplicacion(mensaje)
{
    public string? Codigo { get; } = codigo;
}
