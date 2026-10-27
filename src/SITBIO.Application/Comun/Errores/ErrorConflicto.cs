namespace Sitbio.Application.Comun.Errores;

/// <summary>
/// Conflicto con una regla de negocio (409). El código es estable para que el
/// cliente elija el mensaje, por ejemplo RN-001, RN-004 o RN-009.
/// </summary>
public sealed class ErrorConflicto(string codigo, string mensaje)
    : ErrorAplicacion(mensaje)
{
    public string Codigo { get; } = codigo;
}
