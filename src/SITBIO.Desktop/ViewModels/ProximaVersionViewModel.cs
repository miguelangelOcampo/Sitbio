namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Estado «Disponible en una próxima versión» para las opciones del menú cuya pantalla aún no existe
/// (D-06). Reemplaza a «Pantalla en construcción»: ninguna opción del menú debe llegar a ese texto.
/// </summary>
public sealed class ProximaVersionViewModel(string idOpcion, string detalle)
{
    public const string Titulo = "Disponible en una próxima versión";

    /// <summary>Opciones del menú que muestran este estado, con el texto que explica qué llegará.</summary>
    public static readonly IReadOnlyDictionary<string, string> Opciones = new Dictionary<string, string>
    {
        // Sin fila asignada: el historial ya se consulta en la hoja de vida de cada equipo y en Exportación.
        ["HIST"] = "La consulta del historial de intervenciones de todos los equipos llega con la próxima versión. Mientras tanto, consulta el historial en la hoja de vida de cada equipo o descárgalo desde Exportación.",
    };

    public string IdOpcion { get; } = idOpcion;

    public string Detalle { get; } = detalle;
}
