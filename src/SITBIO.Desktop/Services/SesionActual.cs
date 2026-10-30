namespace Sitbio.Desktop.Services;

/// <summary>
/// Sesión abierta en el cliente. Solo vive en memoria: el token no se guarda en disco
/// (estación compartida, SEC-002) y se descarta al cerrar la aplicación.
/// </summary>
public sealed class SesionActual
{
    public SesionUsuario? Sesion { get; private set; }

    public bool Abierta => Sesion is not null;

    public void Iniciar(SesionUsuario sesion) => Sesion = sesion;

    public void Cerrar() => Sesion = null;
}
