using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sitbio.Application.Analitica;

namespace Sitbio.Infrastructure.Analitica;

/// <summary>
/// Ejecuta el lote de Python (<c>python -m sitbio_analytics --tenant …</c>, ADR-006) para una institución.
/// La cadena de MongoDB va en variables de entorno del proceso, nunca en los argumentos ni en el registro
/// (SEC-008). Configuración opcional: <c>Analitica:Python</c> (ejecutable), <c>Analitica:Carpeta</c> (carpeta
/// <c>analytics</c>) y <c>Analitica:TiempoMaximoMinutos</c> (10 por defecto).
/// </summary>
public sealed partial class EjecutorAnaliticaPython(IConfiguration configuracion, ILogger<EjecutorAnaliticaPython> registro) : IEjecutorAnalitica
{
    public async Task EjecutarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var carpeta = configuracion["Analitica:Carpeta"] is { Length: > 0 } c ? c : BuscarCarpeta()
            ?? throw new InvalidOperationException("No se encontró la carpeta analytics; configura Analitica:Carpeta.");
        var python = configuracion["Analitica:Python"] is { Length: > 0 } p ? p : BuscarPython(carpeta);
        var minutos = int.TryParse(configuracion["Analitica:TiempoMaximoMinutos"], out var m) && m > 0 ? m : 10;

        var inicio = new ProcessStartInfo(python)
        {
            WorkingDirectory = carpeta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        inicio.ArgumentList.Add("-m");
        inicio.ArgumentList.Add("sitbio_analytics");
        inicio.ArgumentList.Add("--tenant");
        inicio.ArgumentList.Add(tenantId.ToString());
        inicio.Environment["SITBIO_MONGO_URI"] = configuracion.GetConnectionString("Mongo")
            ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Mongo.");
        inicio.Environment["SITBIO_MONGO_DB"] = configuracion["Mongo:Database"]
            ?? throw new InvalidOperationException("Falta el nombre de la base en Mongo:Database.");
        inicio.Environment["PYTHONIOENCODING"] = "utf-8";

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromMinutes(minutos));
        using var proceso = Process.Start(inicio) ?? throw new InvalidOperationException($"No se pudo iniciar {python}.");
        var salida = proceso.StandardOutput.ReadToEndAsync(limite.Token);
        var errores = proceso.StandardError.ReadToEndAsync(limite.Token);
        try
        {
            await proceso.WaitForExitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException($"El lote de analítica superó {minutos} minutos.");
        }

        var texto = Ocultar(await salida);
        if (proceso.ExitCode != 0)
        {
            registro.LogError("El lote de analítica del tenant {Tenant} terminó con código {Codigo}: {Error}", tenantId, proceso.ExitCode, Ocultar(await errores));
            throw new InvalidOperationException($"El lote de analítica terminó con código {proceso.ExitCode}.");
        }

        registro.LogInformation("Lote de analítica del tenant {Tenant}: {Salida}", tenantId, texto.Trim());
    }

    /// <summary>Quita usuario y contraseña de cualquier cadena de MongoDB que aparezca en la salida.</summary>
    public static string Ocultar(string texto) => CadenaMongo().Replace(texto, "$1***@");

    private static string? BuscarCarpeta()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidata = Path.Combine(dir.FullName, "analytics");
            if (Directory.Exists(Path.Combine(candidata, "sitbio_analytics")))
            {
                return candidata;
            }
        }

        return null;
    }

    /// <summary>El entorno virtual del repositorio (.venv) si existe; si no, el python del PATH.</summary>
    private static string BuscarPython(string carpeta)
    {
        var raiz = Directory.GetParent(carpeta)?.FullName ?? carpeta;
        foreach (var relativo in new[] { Path.Combine(".venv", "Scripts", "python.exe"), Path.Combine(".venv", "bin", "python") })
        {
            var ruta = Path.Combine(raiz, relativo);
            if (File.Exists(ruta))
            {
                return ruta;
            }
        }

        return "python";
    }

    [GeneratedRegex("(mongodb(?:\\+srv)?://)[^\\s@/]*@")]
    private static partial Regex CadenaMongo();
}
