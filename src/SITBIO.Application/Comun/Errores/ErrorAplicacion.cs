namespace Sitbio.Application.Comun.Errores;

/// <summary>
/// Base de los errores esperados de los casos de uso. El mensaje es seguro para
/// mostrarlo al usuario: no lleva trazas, SQL ni datos personales (DOC-06 11.1).
/// La API traduce cada tipo a su código HTTP con Problem Details.
/// </summary>
public abstract class ErrorAplicacion : Exception
{
    protected ErrorAplicacion(string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
    }
}
