using System.Security.Cryptography;

namespace Sitbio.SemillaDemo;

// Siembra la institución de demostración «Hospital Demo SITBIO» con seis meses de operación simulada.
// Uso (desde la raíz): scripts/semilla-demo.sh [--reemplazar] [--dias N]
/// <summary>Punto de entrada; no usa instrucciones de nivel superior para no chocar con <c>Program</c> de la API.</summary>
public static class EntradaSemilla
{
    public static async Task<int> Main(string[] args)
    {
        var opciones = Opciones.Leer(args);
        if (opciones is null)
        {
            return 2;
        }

        try
        {
            var semilla = new Semilla(opciones);
            await semilla.EjecutarAsync();
            return 0;
        }
        catch (SemillaException error)
        {
            await Console.Error.WriteLineAsync($"No se pudo completar la semilla: {error.Message}");
            return 1;
        }
    }
}

public sealed record Opciones(string Postgres, string Mongo, string BaseMongo, string Clave, bool ClaveGenerada, bool Reemplazar, int Dias, string Salida)
{
    public static Opciones? Leer(string[] args)
    {
        var postgres = Environment.GetEnvironmentVariable("SITBIO_DEMO_POSTGRES");
        var mongo = Environment.GetEnvironmentVariable("SITBIO_DEMO_MONGO");
        if (string.IsNullOrWhiteSpace(postgres) || string.IsNullOrWhiteSpace(mongo))
        {
            Console.Error.WriteLine("Faltan SITBIO_DEMO_POSTGRES y SITBIO_DEMO_MONGO. Usa scripts/semilla-demo.sh, que las arma desde .env.");
            return null;
        }

        var dias = 180;
        var indice = Array.IndexOf(args, "--dias");
        if (indice >= 0 && (indice + 1 >= args.Length || !int.TryParse(args[indice + 1], out dias) || dias is < 30 or > 365))
        {
            Console.Error.WriteLine("--dias debe ser un número entre 30 y 365.");
            return null;
        }

        var clave = Environment.GetEnvironmentVariable("SITBIO_DEMO_CLAVE") ?? string.Empty;
        var generada = string.IsNullOrWhiteSpace(clave);
        if (generada)
        {
            clave = $"Demo-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}-{DateTime.UtcNow.Year}";
        }

        return new Opciones(
            postgres,
            mongo,
            Environment.GetEnvironmentVariable("SITBIO_DEMO_MONGO_DB") is { Length: > 0 } baseMongo ? baseMongo : "sitbio_dev",
            clave,
            generada,
            args.Contains("--reemplazar"),
            dias,
            Environment.GetEnvironmentVariable("SITBIO_DEMO_SALIDA") is { Length: > 0 } salida ? salida : "demo-credenciales.local.txt");
    }
}
