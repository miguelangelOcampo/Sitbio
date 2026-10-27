namespace Sitbio.Application.Comun.Errores;

/// <summary>Sin permiso para la acción o servicio de la institución no activo (403, RNF-003).</summary>
public sealed class ErrorPermiso(string mensaje = "No tiene permiso para realizar esta acción.")
    : ErrorAplicacion(mensaje);
