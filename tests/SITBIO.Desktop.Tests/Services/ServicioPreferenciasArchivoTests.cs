using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.Services;

public sealed class ServicioPreferenciasArchivoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "sitbio-pref-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }
    }

    [Fact]
    public void SinArchivo_SigueElTemaDelSistema()
    {
        var servicio = new ServicioPreferenciasArchivo(_carpeta);

        Assert.Equal(Temas.Sistema, servicio.Leer().Tema);
        Assert.False(File.Exists(servicio.Ruta));
    }

    [Fact]
    public void Guardar_CreaLaCarpetaYLaPreferenciaSeLeeDespues()
    {
        var servicio = new ServicioPreferenciasArchivo(_carpeta);

        servicio.Guardar(new Preferencias(Temas.Oscuro));

        Assert.Equal(Path.Combine(_carpeta, "preferencias.json"), servicio.Ruta);
        Assert.Equal(Temas.Oscuro, new ServicioPreferenciasArchivo(_carpeta).Leer().Tema);
        Assert.False(File.Exists(servicio.Ruta + ".tmp"));
    }

    [Theory]
    [InlineData("{ no es json")]
    [InlineData("")]
    [InlineData("{\"Tema\":\"morado\"}")]
    [InlineData("null")]
    public void ArchivoDanadoOTemaDesconocido_SigueElSistema(string contenido)
    {
        Directory.CreateDirectory(_carpeta);
        var servicio = new ServicioPreferenciasArchivo(_carpeta);
        File.WriteAllText(servicio.Ruta, contenido);

        Assert.Equal(Temas.Sistema, servicio.Leer().Tema);
    }

    [Theory]
    [InlineData(Temas.Sistema, true, Temas.Claro)]
    [InlineData(Temas.Sistema, false, Temas.Oscuro)]
    [InlineData("morado", false, Temas.Oscuro)]
    [InlineData(Temas.Claro, false, Temas.Claro)]
    [InlineData(Temas.Oscuro, true, Temas.Oscuro)]
    public void J10_ElTemaDelSistemaSigueAWindows_YLosElegidosNo(string tema, bool windowsEnClaro, string pintado) =>
        Assert.Equal(pintado, Temas.Resolver(tema, windowsEnClaro));

    [Fact]
    public void J10_ClaroGuardadoAntes_SeConserva()
    {
        Directory.CreateDirectory(_carpeta);
        var servicio = new ServicioPreferenciasArchivo(_carpeta);
        File.WriteAllText(servicio.Ruta, "{\"Tema\":\"claro\"}");

        Assert.Equal(Temas.Claro, servicio.Leer().Tema);
    }

    [Fact]
    public void Guardar_SobrescribeLaPreferenciaAnterior()
    {
        var servicio = new ServicioPreferenciasArchivo(_carpeta);

        servicio.Guardar(new Preferencias(Temas.Oscuro));
        servicio.Guardar(new Preferencias(Temas.Claro));

        Assert.Equal(Temas.Claro, servicio.Leer().Tema);
    }

    [Fact]
    public void NoPoderEscribir_NoLanzaError()
    {
        Directory.CreateDirectory(_carpeta);
        var servicio = new ServicioPreferenciasArchivo(_carpeta);
        Directory.CreateDirectory(servicio.Ruta); // una carpeta con el nombre del archivo impide escribirlo

        var error = Record.Exception(() => servicio.Guardar(new Preferencias(Temas.Oscuro)));

        Assert.Null(error);
        Assert.Equal(Temas.Sistema, servicio.Leer().Tema);
    }
}
