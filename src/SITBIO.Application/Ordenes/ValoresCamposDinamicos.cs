using System.Globalization;
using System.Text.Json;
using Sitbio.Domain.Configuracion;

namespace Sitbio.Application.Ordenes;

/// <summary>
/// Valida los valores de <c>camposDinamicos</c> de una intervención contra la versión activa de
/// <c>configuracion_campos</c> del tipo de equipo (HU-005, HU-006, RF-006): solo los campos definidos,
/// los obligatorios presentes y cada valor con su tipo de dato. Los errores van con la clave
/// <c>camposDinamicos.{nombre}</c> para mostrarlos junto al campo.
/// </summary>
public static class ValoresCamposDinamicos
{
    public const int LongitudMaximaTexto = 500;
    public const string Prefijo = "camposDinamicos";

    public static (IReadOnlyDictionary<string, object> Valores, IDictionary<string, string[]> Errores) Validar(
        IReadOnlyList<CampoDinamico> campos,
        IReadOnlyDictionary<string, JsonElement>? valores)
    {
        ArgumentNullException.ThrowIfNull(campos);
        var recibidos = valores ?? new Dictionary<string, JsonElement>();
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tipados = new Dictionary<string, object>(StringComparer.Ordinal);

        var definidos = campos.Where(c => c.Nombre is not null).ToDictionary(c => c.Nombre!, StringComparer.Ordinal);
        foreach (var nombre in recibidos.Keys.Where(k => !definidos.ContainsKey(k)))
        {
            errores[$"{Prefijo}.{nombre}"] = ["Este campo no está en la configuración vigente del tipo de equipo. Recarga el formulario."];
        }

        foreach (var campo in definidos.Values)
        {
            var etiqueta = campo.Etiqueta ?? campo.Nombre!;
            var clave = $"{Prefijo}.{campo.Nombre}";
            if (!recibidos.TryGetValue(campo.Nombre!, out var valor) || Vacio(valor))
            {
                if (campo.Obligatorio)
                {
                    errores[clave] = [$"Completa «{etiqueta}»."];
                }

                continue;
            }

            var (tipado, error) = Convertir(campo, etiqueta, valor);
            if (error is not null)
            {
                errores[clave] = [error];
            }
            else
            {
                tipados[campo.Nombre!] = tipado!;
            }
        }

        return (tipados, errores);
    }

    private static bool Vacio(JsonElement valor) =>
        valor.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || (valor.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(valor.GetString()));

    private static (object? Valor, string? Error) Convertir(CampoDinamico campo, string etiqueta, JsonElement valor)
    {
        switch (campo.TipoDato)
        {
            case TiposDatoCampo.Numero:
                return valor.ValueKind == JsonValueKind.Number && valor.TryGetInt64(out var entero)
                    ? (entero, null)
                    : (null, $"«{etiqueta}» debe ser un número entero.");
            case TiposDatoCampo.NumeroDecimal:
                return valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero) && double.IsFinite(numero)
                    ? (numero, null)
                    : (null, $"«{etiqueta}» debe ser un número.");
            case TiposDatoCampo.Booleano:
                return valor.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (valor.GetBoolean(), null)
                    : (null, $"«{etiqueta}» debe ser sí o no.");
            case TiposDatoCampo.Fecha:
                return valor.ValueKind == JsonValueKind.String
                       && DateOnly.TryParseExact(valor.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                    ? (fecha, null)
                    : (null, $"«{etiqueta}» debe ser una fecha válida.");
            case TiposDatoCampo.Seleccion:
                var opcion = valor.ValueKind == JsonValueKind.String
                    ? (campo.Opciones ?? []).FirstOrDefault(o => string.Equals(o, valor.GetString()!.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    : null;
                return opcion is not null
                    ? (opcion, null)
                    : (null, $"Elige una de las opciones de «{etiqueta}».");
            default:
                if (valor.ValueKind != JsonValueKind.String)
                {
                    return (null, $"«{etiqueta}» debe ser texto.");
                }

                var texto = valor.GetString()!.Trim();
                return texto.Length <= LongitudMaximaTexto
                    ? (texto, null)
                    : (null, $"«{etiqueta}» admite máximo {LongitudMaximaTexto} caracteres.");
        }
    }
}
