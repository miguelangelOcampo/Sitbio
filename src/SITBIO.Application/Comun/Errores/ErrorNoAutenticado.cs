namespace Sitbio.Application.Comun.Errores;

/// <summary>
/// La solicitud no trae una identidad válida o las credenciales no sirven (401).
/// <see cref="Codigo"/> indica el mensaje del catálogo (MSG-001, MSG-002) que muestra el cliente.
/// </summary>
public sealed class ErrorNoAutenticado(string mensaje = "Debe iniciar sesión para continuar.", string? codigo = null)
    : ErrorAplicacion(mensaje)
{
    public string? Codigo { get; } = codigo;
}
