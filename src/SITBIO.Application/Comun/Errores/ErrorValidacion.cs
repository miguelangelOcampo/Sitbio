namespace Sitbio.Application.Comun.Errores;

/// <summary>Datos de entrada inválidos (400), con los mensajes agrupados por campo.</summary>
public sealed class ErrorValidacion(IDictionary<string, string[]> errores)
    : ErrorAplicacion("Uno o más datos no son válidos.")
{
    public IDictionary<string, string[]> Errores { get; } = errores;

    public static ErrorValidacion DeCampo(string campo, string mensaje) => new(new Dictionary<string, string[]> { [campo] = [mensaje] });
}
