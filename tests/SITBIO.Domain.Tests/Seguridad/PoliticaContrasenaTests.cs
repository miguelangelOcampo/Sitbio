using Sitbio.Domain.Seguridad;

namespace Sitbio.Domain.Tests.Seguridad;

/// <summary>HU-013 (RF-012): reglas de la contraseña que define el usuario (UI-002).</summary>
public class PoliticaContrasenaTests
{
    private static IReadOnlyList<string> Revisar(string clave) =>
        PoliticaContrasena.Incumplidas(clave, "Laura María", "Gómez de la Torre", "laura.gomez@clinica.co");

    [Fact]
    public void ClaveQueCumpleTodo_NoTieneReglasIncumplidas()
    {
        Assert.Empty(Revisar("Monitor#2026uci"));
    }

    [Theory]
    [InlineData("Ab1#cdefg", PoliticaContrasena.ReglaLongitud)]
    [InlineData("monitor#2026uci", PoliticaContrasena.ReglaMayusculaMinuscula)]
    [InlineData("MONITOR#2026UCI", PoliticaContrasena.ReglaMayusculaMinuscula)]
    [InlineData("Monitor#UciAdultos", PoliticaContrasena.ReglaNumero)]
    [InlineData("Monitor2026Uci", PoliticaContrasena.ReglaSimbolo)]
    public void CadaReglaIncumplida_SeReporta(string clave, string regla)
    {
        Assert.Equal([regla], Revisar(clave));
    }

    [Theory]
    [InlineData("Laura#2026abc")]
    [InlineData("xGOMEZ#2026a")]
    [InlineData("Gomez*2026abc")]
    [InlineData("Maria*2026abc")]
    [InlineData("Torre*2026abc")]
    public void NombreOApellido_SinImportarMayusculasNiTildes_SeRechaza(string clave)
    {
        Assert.Contains(PoliticaContrasena.ReglaDatosPersonales, Revisar(clave));
    }

    [Fact]
    public void ElUsuarioDelCorreo_SeRechaza()
    {
        var incumplidas = PoliticaContrasena.Incumplidas("Xx#1lgomez2026", "Ana", "Ruiz", "lgomez@clinica.co");

        Assert.Equal([PoliticaContrasena.ReglaDatosPersonales], incumplidas);
    }

    [Fact]
    public void PartesCortasDelNombre_NoProducenFalsosRechazos()
    {
        // «de» y «la» del apellido no cuentan como datos personales.
        Assert.Empty(Revisar("Bomba#de2026la"));
    }

    [Fact]
    public void VariasReglasIncumplidas_SeReportanTodasEnOrden()
    {
        Assert.Equal(
            [PoliticaContrasena.ReglaLongitud, PoliticaContrasena.ReglaMayusculaMinuscula, PoliticaContrasena.ReglaNumero, PoliticaContrasena.ReglaSimbolo],
            Revisar("abc"));
    }
}
