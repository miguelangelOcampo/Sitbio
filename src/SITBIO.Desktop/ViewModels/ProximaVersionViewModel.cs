namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Estado «Disponible en una próxima versión» para las opciones del menú cuya pantalla aún no existe
/// (D-06). Reemplaza a «Pantalla en construcción»: ninguna opción del menú debe llegar a ese texto.
/// </summary>
public sealed class ProximaVersionViewModel(string idOpcion, string detalle)
{
    public const string Titulo = "Disponible en una próxima versión";

    /// <summary>
    /// Opciones del menú que muestran este estado, con el texto que explica qué llegará. Hoy ninguna: el
    /// historial de intervenciones (HIST) ya tiene su pantalla.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Opciones = new Dictionary<string, string>();

    public string IdOpcion { get; } = idOpcion;

    public string Detalle { get; } = detalle;
}
