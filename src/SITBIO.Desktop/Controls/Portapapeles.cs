using System.Runtime.InteropServices;
using System.Windows;

namespace Sitbio.Desktop.Controls;

/// <summary>Escribe en el portapapeles de Windows; las pruebas reemplazan <see cref="Escribir"/>.</summary>
public static class Portapapeles
{
    /// <summary>Destino del texto copiado; por defecto el portapapeles del sistema.</summary>
    public static Action<string> Escribir { get; set; } = EscribirEnSistema;

    public static void Copiar(string? texto)
    {
        if (!string.IsNullOrEmpty(texto))
        {
            Escribir(texto);
        }
    }

    private static void EscribirEnSistema(string texto)
    {
        // Otro programa puede tener el portapapeles abierto un instante: se reintenta antes de rendirse.
        for (var intento = 0; intento < 5; intento++)
        {
            try
            {
                Clipboard.SetText(texto);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
    }
}
