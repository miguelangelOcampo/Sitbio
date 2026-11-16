using System.Text.Json;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Configuracion;

namespace Sitbio.Application.Tests.Ordenes;

/// <summary>Valores de <c>camposDinamicos</c> contra la versión activa (HU-005, HU-006): tipo de dato, obligatorios y campos desconocidos.</summary>
public class ValoresCamposDinamicosTests
{
    private static (IReadOnlyDictionary<string, object> Valores, IDictionary<string, string[]> Errores) Validar(CampoDinamico campo, string json) =>
        ValoresCamposDinamicos.Validar([campo], JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json));

    [Theory]
    [InlineData(TiposDatoCampo.Numero, "12", 12L)]
    [InlineData(TiposDatoCampo.NumeroDecimal, "12", 12d)]
    [InlineData(TiposDatoCampo.NumeroDecimal, "0.75", 0.75d)]
    [InlineData(TiposDatoCampo.Booleano, "false", false)]
    [InlineData(TiposDatoCampo.Texto, "\"  Sin novedad  \"", "Sin novedad")]
    public void ValorDelTipoCorrecto_SeGuardaTipado(string tipo, string json, object esperado)
    {
        var (valores, errores) = Validar(new CampoDinamico("c", "Campo", tipo, true), $$"""{"c": {{json}}}""");

        Assert.Empty(errores);
        Assert.Equal(esperado, valores["c"]);
    }

    [Theory]
    [InlineData(TiposDatoCampo.Numero, "12.5")]
    [InlineData(TiposDatoCampo.Numero, "\"12\"")]
    [InlineData(TiposDatoCampo.NumeroDecimal, "\"abc\"")]
    [InlineData(TiposDatoCampo.Booleano, "\"si\"")]
    [InlineData(TiposDatoCampo.Fecha, "\"01/05/2027\"")]
    [InlineData(TiposDatoCampo.Fecha, "\"2027-02-30\"")]
    [InlineData(TiposDatoCampo.Texto, "5")]
    public void ValorDeOtroTipo_SeRechazaJuntoAlCampo(string tipo, string json)
    {
        var (valores, errores) = Validar(new CampoDinamico("c", "Campo", tipo, false), $$"""{"c": {{json}}}""");

        Assert.Empty(valores);
        Assert.Contains("«Campo»", Assert.Single(errores["camposDinamicos.c"]));
    }

    [Fact]
    public void Fecha_Y_Seleccion_UsanElValorCanonico()
    {
        var campos = new[]
        {
            new CampoDinamico("vence", "Vence", TiposDatoCampo.Fecha, false),
            new CampoDinamico("resultado", "Resultado", TiposDatoCampo.Seleccion, false, ["Pasa", "No pasa"]),
        };

        var (valores, errores) = ValoresCamposDinamicos.Validar(
            campos, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"vence": "2027-05-01", "resultado": " no PASA "}"""));

        Assert.Empty(errores);
        Assert.Equal((new DateOnly(2027, 5, 1), "No pasa"), ((DateOnly)valores["vence"], (string)valores["resultado"]));
    }

    [Fact]
    public void TextoMuyLargo_SeRechaza()
    {
        var (_, errores) = Validar(new CampoDinamico("c", "Notas", TiposDatoCampo.Texto, false), $$"""{"c": "{{new string('a', ValoresCamposDinamicos.LongitudMaximaTexto + 1)}}"}""");

        Assert.True(errores.ContainsKey("camposDinamicos.c"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"c": null}""")]
    [InlineData("""{"c": "  "}""")]
    public void Vacio_SoloSeExigeSiEsObligatorio(string json)
    {
        Assert.Empty(Validar(new CampoDinamico("c", "Campo", TiposDatoCampo.Texto, false), json).Errores);
        Assert.Equal(["Completa «Campo»."], Validar(new CampoDinamico("c", "Campo", TiposDatoCampo.Texto, true), json).Errores["camposDinamicos.c"]);
    }

    [Fact]
    public void CampoQueNoEstaEnLaVersion_SeRechaza_YSinValoresNoFalla()
    {
        var (_, errores) = Validar(new CampoDinamico("c", "Campo", TiposDatoCampo.Texto, false), """{"otro": 1}""");
        Assert.Equal(["camposDinamicos.otro"], errores.Keys);

        Assert.Empty(ValoresCamposDinamicos.Validar([], null).Errores);
    }
}
