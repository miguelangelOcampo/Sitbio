using System.Globalization;
using System.Text;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Ícono por tipo de equipo (E-10; prototipo <c>components/ui.tsx</c>, EqIcon). Los tipos son un catálogo de cada
/// institución (texto libre), así que se reconocen por palabras clave sin tildes ni mayúsculas; sin coincidencia, el
/// ícono genérico. Devuelve la clave del recurso en <c>Themes/Icons.xaml</c>.
/// </summary>
public static class IconosTipoEquipo
{
    public const string Generico = "Icono.Equipo";

    // El orden importa: «monitor fetal» o «bomba de infusión» se deciden por la primera regla que coincide.
    private static readonly (string Clave, string[] Palabras)[] Reglas =
    [
        ("Icono.Tipo.BombaInfusion", ["bomba", "infusion", "jeringa", "perfusor"]),
        ("Icono.Tipo.Desfibrilador", ["desfibrilador", " dea "]),
        ("Icono.Tipo.Electrocardiografo", ["electrocardiogra", " ecg ", " ekg ", "holter"]),
        ("Icono.Tipo.Ventilador", ["ventilador", "respirador", "cpap", "bipap"]),
        ("Icono.Tipo.Monitor", ["monitor", "signos vitales", "multiparametro", "oximetro", "pulsioximetro"]),
        ("Icono.Tipo.Autoclave", ["autoclave", "esteriliza"]),
        ("Icono.Tipo.Balanza", ["balanza", "bascula", "pesa bebe"]),
        ("Icono.Tipo.Electrobisturi", ["electrobisturi", "bisturi", "electrocauterio", "unidad electroquirurgica"]),
        ("Icono.Tipo.Tensiometro", ["tensiometro", "esfigmomanometro", "presion arterial", "manometro"]),
        ("Icono.Tipo.Estetoscopio", ["estetoscopio", "fonendoscopio"]),
    ];

    /// <summary>Todas las claves que puede devolver <see cref="Clave"/>, incluida la genérica.</summary>
    public static IReadOnlyList<string> Claves { get; } = [.. Reglas.Select(r => r.Clave), Generico];

    public static string Clave(string? tipoEquipo)
    {
        if (string.IsNullOrWhiteSpace(tipoEquipo))
        {
            return Generico;
        }

        var texto = " " + Normalizar(tipoEquipo) + " ";
        foreach (var (clave, palabras) in Reglas)
        {
            if (palabras.Any(p => texto.Contains(p, StringComparison.Ordinal)))
            {
                return clave;
            }
        }

        return Generico;
    }

    /// <summary>Minúsculas, sin tildes y con espacios simples: «Báscula  Pesa-bebé» → «bascula pesa bebe».</summary>
    private static string Normalizar(string texto)
    {
        var descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var salida = new StringBuilder(descompuesto.Length);
        var espacio = false;
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var separador = char.IsWhiteSpace(c) || c is '-' or '_' or '/' or '(' or ')' or '.';
            if (separador)
            {
                if (!espacio)
                {
                    salida.Append(' ');
                }

                espacio = true;
                continue;
            }

            salida.Append(c);
            espacio = false;
        }

        return salida.ToString().Trim();
    }
}
