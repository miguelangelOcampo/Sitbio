namespace Sitbio.Api.Errores;

/// <summary>
/// Asigna a cada solicitud un identificador de correlación: toma el de la cabecera
/// X-Correlation-ID si es válido o genera uno nuevo, lo devuelve en la respuesta y
/// lo agrega al contexto de los registros (DOC-06 9.3 y 11).
/// </summary>
public sealed class CorrelacionMiddleware(RequestDelegate siguiente, ILogger<CorrelacionMiddleware> logger)
{
    public const string Cabecera = "X-Correlation-ID";

    private const string Clave = "sitbio.correlacion";
    private const int LongitudMaxima = 64;

    public async Task InvokeAsync(HttpContext http)
    {
        var recibido = http.Request.Headers[Cabecera].ToString();
        var id = EsValido(recibido) ? recibido : Guid.NewGuid().ToString("N");
        http.Items[Clave] = id;

        // OnStarting: el manejador de errores limpia las cabeceras antes de responder.
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[Cabecera] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await siguiente(http);
        }
    }

    public static string? Obtener(HttpContext http) => http.Items[Clave] as string;

    private static bool EsValido(string valor) =>
        valor.Length is > 0 and <= LongitudMaxima
        && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
