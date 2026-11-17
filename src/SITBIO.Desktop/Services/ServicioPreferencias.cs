using System.IO;
using System.Text.Json;

namespace Sitbio.Desktop.Services;

/// <summary>Temas de la interfaz (Themes/Light.xaml y Themes/Dark.xaml).</summary>
public static class Temas
{
    public const string Claro = "claro";
    public const string Oscuro = "oscuro";

    /// <summary>Un valor desconocido o vacío vuelve al tema claro.</summary>
    public static string Normalizar(string? tema) => tema == Oscuro ? Oscuro : Claro;
}

/// <summary>Preferencias de la estación (DOC-06, Tabla 44). No son datos de la cuenta: no viajan a la API.</summary>
public sealed record Preferencias(string Tema = Temas.Claro);

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
