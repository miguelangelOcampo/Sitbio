using System.IO;
using System.Text.Json;

namespace Sitbio.Desktop.Services;

/// <summary>Temas de la interfaz (Themes/Light.xaml y Themes/Dark.xaml).</summary>
public static class Temas
{
    public const string Claro = "claro";
    public const string Oscuro = "oscuro";

    /// <summary>J-10: sigue el modo de aplicaciones de Windows (claro u oscuro) y cambia con él.</summary>
    public const string Sistema = "sistema";

    /// <summary>Un valor desconocido o vacío sigue al sistema.</summary>
    public static string Normalizar(string? tema) => tema is Claro or Oscuro ? tema : Sistema;

    /// <summary>El tema que se pinta: el elegido o, con «Sistema», el de Windows.</summary>
    public static string Resolver(string? tema, bool sistemaClaro) => Normalizar(tema) switch
    {
        Sistema => sistemaClaro ? Claro : Oscuro,
        var elegido => elegido,
    };
}

/// <summary>Preferencias de la estación (DOC-06, Tabla 44). No son datos de la cuenta: no viajan a la API.</summary>
/// <param name="VerFotosEquipo">RF-031: true muestra la foto del equipo cuando tiene una; false, siempre el ícono del tipo.</param>
/// <param name="RevisionTiquetes">
/// J-07: por usuario (id de la sesión), fecha del último cambio de sus tiquetes que ya vio en la campana. Es por
/// usuario porque la estación puede ser compartida.
/// </param>
public sealed record Preferencias(string Tema = Temas.Sistema, bool VerFotosEquipo = true, IReadOnlyDictionary<string, DateTimeOffset>? RevisionTiquetes = null)
{
    /// <summary>Último cambio visto por el usuario; null si nunca abrió la campana en esta estación.</summary>
    public DateTimeOffset? RevisionDe(Guid usuarioId) =>
        RevisionTiquetes?.TryGetValue(usuarioId.ToString(), out var fecha) == true ? fecha : null;

    /// <summary>Las mismas preferencias con la fecha vista de ese usuario.</summary>
    public Preferencias ConRevision(Guid usuarioId, DateTimeOffset fecha)
    {
        var revision = RevisionTiquetes is null ? new Dictionary<string, DateTimeOffset>() : new Dictionary<string, DateTimeOffset>(RevisionTiquetes);
        revision[usuarioId.ToString()] = fecha;
        return this with { RevisionTiquetes = revision };
    }
}

/// <summary>Lee y guarda las preferencias locales del cliente.</summary>
public interface IServicioPreferencias
{
    Preferencias Leer();

    void Guardar(Preferencias preferencias);
}

/// <summary>
/// Preferencias en <c>%LOCALAPPDATA%\SITBIO\preferencias.json</c>. Un archivo que falta, está dañado o no se puede
/// escribir nunca impide usar la aplicación: se sigue con los valores por omisión.
/// </summary>
public sealed class ServicioPreferenciasArchivo(string? carpeta = null) : IServicioPreferencias
{
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    public string Ruta { get; } = Path.Combine(
        carpeta ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SITBIO"),
        "preferencias.json");

    public Preferencias Leer()
    {
        try
        {
            if (!File.Exists(Ruta))
            {
                return new Preferencias();
            }

            var leidas = JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(Ruta), Opciones) ?? new Preferencias();
            return leidas with { Tema = Temas.Normalizar(leidas.Tema) };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Preferencias();
        }
    }

    public void Guardar(Preferencias preferencias)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            var temporal = Ruta + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(preferencias with { Tema = Temas.Normalizar(preferencias.Tema) }, Opciones));
            File.Move(temporal, Ruta, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // La preferencia ya se aplicó en pantalla; si no se pudo guardar, solo se pierde al reiniciar.
        }
    }
}

/// <summary>Cambia el tema de la aplicación abierta.</summary>
public interface ITemaAplicacion
{
    void Aplicar(string tema);
}
