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
        // HU-016: E-07 a E-09 la reemplazan por la pantalla Importar inventario.
        ["UI-008"] = "La importación masiva de equipos desde CSV o Excel, con plantilla descargable, revisión por fila e informe de errores, llega con la próxima versión.",
        // D-01 y D-02 la reemplazan por el panel de inicio por rol.
        ["UI-003"] = "El panel de inicio con los indicadores del día, la atención requerida y la actividad de mantenimiento llega con la próxima versión.",
        // Sin fila asignada: el historial ya se consulta en la hoja de vida de cada equipo y en Exportación.
        ["HIST"] = "La consulta del historial de intervenciones de todos los equipos llega con la próxima versión. Mientras tanto, consulta el historial en la hoja de vida de cada equipo o descárgalo desde Exportación.",
    };

    public string IdOpcion { get; } = idOpcion;

    public string Detalle { get; } = detalle;
}
