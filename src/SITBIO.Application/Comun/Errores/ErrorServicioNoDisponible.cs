namespace Sitbio.Application.Comun.Errores;

/// <summary>Una dependencia no responde, por ejemplo MongoDB al guardar (503); el cliente puede reintentar.</summary>
public sealed class ErrorServicioNoDisponible(Exception? interna = null)
    : ErrorAplicacion("El servicio no está disponible en este momento. Intente de nuevo.", interna);
