namespace Sitbio.Application.Comun.Errores;

/// <summary>
/// El recurso no existe en el tenant del usuario (404). También se usa cuando el
/// recurso es de otra institución, para no revelar que existe (SEC-003).
/// </summary>
public sealed class ErrorNoEncontrado(string recurso)
    : ErrorAplicacion($"{recurso} no existe.");
