using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Vista provisional después del inicio de sesión; el commit 11 la reemplaza por el menú por rol.</summary>
public sealed class SesionIniciadaViewModel(SesionUsuario sesion)
{
    public string Rol { get; } = sesion.Rol;

    public bool DebeCambiarClave { get; } = sesion.DebeCambiarClave;
}
