using System.Globalization;
using System.Text;

namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Reglas de la contraseña que el usuario define (RF-012, HU-013, UI-002). Son las mismas que
/// el cliente muestra mientras se escribe; la API las vuelve a aplicar antes de guardar.
/// </summary>
public static class PoliticaContrasena
{
    public const int LongitudMinima = 10;

    public const string ReglaLongitud = "Al menos 10 caracteres.";
    public const string ReglaMayusculaMinuscula = "Una letra mayúscula y una minúscula.";
    public const string ReglaNumero = "Al menos un número.";
    public const string ReglaSimbolo = "Al menos un símbolo (por ejemplo * # $ !).";
    public const string ReglaDatosPersonales = "No contiene tu nombre, tu apellido ni tu correo.";

    // Partes del nombre o del correo de una o dos letras («de», «la») darían falsos rechazos
    private const int LongitudMinimaParte = 3;

    /// <summary>Reglas que la contraseña no cumple, en el orden en que el cliente las muestra.</summary>
    public static IReadOnlyList<string> Incumplidas(string contrasena, string nombres, string apellidos, string correo)
    {
        var incumplidas = new List<string>();
        if (contrasena.Length < LongitudMinima)
        {
            incumplidas.Add(ReglaLongitud);
        }

        if (!contrasena.Any(char.IsUpper) || !contrasena.Any(char.IsLower))
        {
            incumplidas.Add(ReglaMayusculaMinuscula);
        }

        if (!contrasena.Any(char.IsDigit))
        {
            incumplidas.Add(ReglaNumero);
        }

        if (!contrasena.Any(c => !char.IsLetterOrDigit(c)))
        {
            incumplidas.Add(ReglaSimbolo);
        }

        if (ContieneDatosPersonales(contrasena, nombres, apellidos, correo))
        {
            incumplidas.Add(ReglaDatosPersonales);
        }

        return incumplidas;
    }

    private static bool ContieneDatosPersonales(string contrasena, string nombres, string apellidos, string correo)
    {
        var clave = Simplificar(contrasena);
        var usuarioCorreo = correo.Split('@', 2)[0];
        var partes = $"{nombres} {apellidos} {usuarioCorreo}"
            .Split([' ', '.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Append(usuarioCorreo)
            .Select(Simplificar)
            .Where(p => p.Length >= LongitudMinimaParte);

        return partes.Any(p => clave.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>Minúsculas y sin tildes, para que «Gómez» y «gomez» cuenten igual.</summary>
    private static string Simplificar(string texto)
    {
        var descompuesto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(c);
            }
        }

        return resultado.ToString().Normalize(NormalizationForm.FormC);
    }
}
