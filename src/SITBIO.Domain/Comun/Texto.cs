namespace Sitbio.Domain.Comun;

/// <summary>Validación de textos de las entidades: sin espacios a los lados y con longitud máxima del esquema.</summary>
public static class Texto
{
    public static string Requerido(string? valor, string campo, int longitudMaxima)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException($"El campo {campo} es obligatorio.", campo);
        }

        return Longitud(valor.Trim(), campo, longitudMaxima);
    }

    public static string? Opcional(string? valor, int longitudMaxima, string campo = "texto") =>
        string.IsNullOrWhiteSpace(valor) ? null : Longitud(valor.Trim(), campo, longitudMaxima);

    private static string Longitud(string valor, string campo, int longitudMaxima) =>
        valor.Length <= longitudMaxima
            ? valor
            : throw new ArgumentException($"El campo {campo} admite máximo {longitudMaxima} caracteres.", campo);
}
