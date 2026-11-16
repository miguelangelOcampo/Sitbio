using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sitbio.Domain.Configuracion;

/// <summary>Tipos de dato de un campo dinámico (validador de <c>configuracion_campos</c>, script 02).</summary>
public static class TiposDatoCampo
{
    public const string Numero = "numero";
    public const string NumeroDecimal = "numero_decimal";
    public const string Texto = "texto";
    public const string Booleano = "booleano";
    public const string Fecha = "fecha";
    public const string Seleccion = "seleccion";

    public static IReadOnlyList<string> Todos { get; } = [Numero, NumeroDecimal, Texto, Booleano, Fecha, Seleccion];

    public static bool EsNumerico(string tipo) => tipo is Numero or NumeroDecimal;
}

/// <summary>Estado de una versión: solo una activa por institución y tipo de equipo.</summary>
public static class EstadosConfiguracion
{
    public const string Activa = "activa";
    public const string Historica = "historica";
}

/// <summary>
/// Campo dinámico del formulario de intervención (RF-006, HU-006). <see cref="Nombre"/> es la clave
/// que se guarda dentro de <c>camposDinamicos</c> de la intervención; <see cref="Etiqueta"/> es lo que
/// ve el técnico. Las opciones solo aplican a la selección y la unidad solo a los números.
/// </summary>
public sealed record CampoDinamico(
    string? Nombre,
    string? Etiqueta,
    string? TipoDato,
    bool Obligatorio = false,
    IReadOnlyList<string>? Opciones = null,
    string? Unidad = null)
{
    // Igualdad por contenido (las listas se comparan elemento a elemento) para detectar una
    // publicación sin cambios.
    public bool Equals(CampoDinamico? otro) =>
        otro is not null
        && Nombre == otro.Nombre
        && Etiqueta == otro.Etiqueta
        && TipoDato == otro.TipoDato
        && Obligatorio == otro.Obligatorio
        && Unidad == otro.Unidad
        && (Opciones ?? []).SequenceEqual(otro.Opciones ?? []);

    public override int GetHashCode() => HashCode.Combine(Nombre, Etiqueta, TipoDato, Obligatorio, Unidad, Opciones?.Count ?? 0);
}

/// <summary>
/// Reglas de una versión de campos dinámicos. Normaliza lo que llega (espacios, nombre en
/// minúsculas, opciones sin repetir) y devuelve los errores por campo con la clave del cuerpo
/// (<c>campos[2].etiqueta</c>), para que el cliente los muestre junto al campo.
/// </summary>
public static partial class ReglasCamposDinamicos
{
    public const int MaximoCampos = 40;
    public const int LongitudMinimaEtiqueta = 3;
    public const int LongitudMaximaEtiqueta = 80;
    public const int LongitudMaximaNombre = 50;
    public const int LongitudMaximaUnidad = 20;
    public const int MinimoOpciones = 2;
    public const int MaximoOpciones = 30;
    public const int LongitudMaximaOpcion = 60;

    /// <summary>Clave en minúsculas, sin tildes y con guion bajo a partir de la etiqueta («Tasa de goteo» → tasa_de_goteo).</summary>
    public static string NombreDesde(string etiqueta)
    {
        var sinTildes = new StringBuilder();
        foreach (var letra in etiqueta.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra) != UnicodeCategory.NonSpacingMark)
            {
                sinTildes.Append(letra);
            }
        }

        var nombre = NoAlfanumerico().Replace(sinTildes.ToString().ToLowerInvariant(), "_").Trim('_');
        if (nombre.Length > 0 && char.IsDigit(nombre[0]))
        {
            nombre = "campo_" + nombre;
        }

        return nombre.Length > LongitudMaximaNombre ? nombre[..LongitudMaximaNombre].TrimEnd('_') : nombre;
    }

    public static (IReadOnlyList<CampoDinamico> Campos, IDictionary<string, string[]> Errores) Normalizar(IReadOnlyList<CampoDinamico?>? campos)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var normalizados = new List<CampoDinamico>();
        if (campos is null || campos.Count == 0)
        {
            errores["campos"] = ["Agrega al menos un campo."];
            return (normalizados, errores);
        }

        if (campos.Count > MaximoCampos)
        {
            errores["campos"] = [$"Se admiten máximo {MaximoCampos} campos por tipo de equipo."];
            return (normalizados, errores);
        }

        var nombres = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < campos.Count; i++)
        {
            var clave = $"campos[{i}]";
            if (campos[i] is not { } campo)
            {
                errores[clave] = ["El campo está vacío."];
                continue;
            }

            var etiqueta = campo.Etiqueta?.Trim() ?? string.Empty;
            if (etiqueta.Length < LongitudMinimaEtiqueta)
            {
                errores[$"{clave}.etiqueta"] = ["Escribe la etiqueta visible (al menos 3 caracteres)."];
            }
            else if (etiqueta.Length > LongitudMaximaEtiqueta)
            {
                errores[$"{clave}.etiqueta"] = [$"La etiqueta admite máximo {LongitudMaximaEtiqueta} caracteres."];
            }

            var nombre = string.IsNullOrWhiteSpace(campo.Nombre) ? NombreDesde(etiqueta) : campo.Nombre.Trim();
            if (!PatronNombre().IsMatch(nombre))
            {
                errores[$"{clave}.nombre"] = ["Usa minúsculas, números y guion bajo, empezando por una letra (por ejemplo tasa_goteo)."];
            }
            else if (!nombres.Add(nombre))
            {
                errores[$"{clave}.nombre"] = ["Ya hay un campo con ese nombre."];
            }

            var tipo = campo.TipoDato?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!TiposDatoCampo.Todos.Contains(tipo))
            {
                errores[$"{clave}.tipoDato"] = ["Elige un tipo de dato: número, número decimal, texto, sí/no, fecha o selección."];
            }

            IReadOnlyList<string>? opciones = null;
            if (tipo == TiposDatoCampo.Seleccion)
            {
                var lista = (campo.Opciones ?? [])
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => o.Trim())
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (lista.Count < MinimoOpciones)
                {
                    errores[$"{clave}.opciones"] = ["Escribe al menos dos opciones."];
                }
                else if (lista.Count > MaximoOpciones || lista.Any(o => o.Length > LongitudMaximaOpcion))
                {
                    errores[$"{clave}.opciones"] = [$"Se admiten máximo {MaximoOpciones} opciones de hasta {LongitudMaximaOpcion} caracteres."];
                }

                opciones = lista;
            }

            // La unidad solo se muestra junto a los números (prototipo); en otros tipos se descarta.
            var unidad = TiposDatoCampo.EsNumerico(tipo) && !string.IsNullOrWhiteSpace(campo.Unidad) ? campo.Unidad.Trim() : null;
            if (unidad is { Length: > LongitudMaximaUnidad })
            {
                errores[$"{clave}.unidad"] = [$"La unidad admite máximo {LongitudMaximaUnidad} caracteres."];
            }

            normalizados.Add(new CampoDinamico(nombre, etiqueta, tipo, campo.Obligatorio, opciones, unidad));
        }

        return (normalizados, errores);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,49}$")]
    private static partial Regex PatronNombre();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NoAlfanumerico();
}
