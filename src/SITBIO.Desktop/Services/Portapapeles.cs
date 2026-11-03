using System.Runtime.InteropServices;
using System.Windows;

namespace Sitbio.Desktop.Services;

/// <summary>Copiar texto (por ejemplo, la clave temporal); abstraído para probar los ViewModels.</summary>
public interface IPortapapeles
{
    /// <summary>Devuelve false si otro programa tiene ocupado el portapapeles.</summary>
    bool Copiar(string texto);
}

public sealed class PortapapelesWpf : IPortapapeles
{
    public bool Copiar(string texto)
    {
        try
        {
            Clipboard.SetText(texto);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
