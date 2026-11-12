using Sitbio.Domain.Configuracion;

namespace Sitbio.Domain.Tests.Configuracion;

/// <summary>Reglas de una versión de campos dinámicos (HU-006, RF-006; validador de configuracion_campos).</summary>
public class ReglasCamposDinamicosTests
{
    private static (IReadOnlyList<CampoDinamico> Campos, IDictionary<string, string[]> Errores) Normalizar(params CampoDinamico?[] campos) =>
        ReglasCamposDinamicos.Normalizar(campos);

    [Theory]
    [InlineData("Tasa de goteo", "tasa_de_goteo")]
    [InlineData("Saturación O₂", "saturacion_o")]
    [InlineData("  ¿Alarma activa?  ", "alarma_activa")]
    [InlineData("12 derivaciones", "campo_12_derivaciones")]
    public void NombreDesde_GeneraLaClaveSinTildesNiEspacios(string etiqueta, string esperado) =>
        Assert.Equal(esperado, ReglasCamposDinamicos.NombreDesde(etiqueta));

    [Fact]
    public void Normalizar_LimpiaEspaciosYDerivaElNombre()
    {
        var (campos, errores) = Normalizar(new CampoDinamico(null, "  Tasa de goteo ", " NUMERO_DECIMAL ", true, Unidad: " ml/h "));

        Assert.Empty(errores);
        Assert.Equal(new CampoDinamico("tasa_de_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h"), Assert.Single(campos));
    }

    [Fact]
    public void Normalizar_SinCampos_PideAlMenosUno()
    {
        Assert.Equal(["Agrega al menos un campo."], ReglasCamposDinamicos.Normalizar([]).Errores["campos"]);
        Assert.Equal(["Agrega al menos un campo."], ReglasCamposDinamicos.Normalizar(null).Errores["campos"]);
    }

    [Fact]
    public void Normalizar_MasDelMaximo_LoRechaza()
    {
        var campos = Enumerable.Range(1, ReglasCamposDinamicos.MaximoCampos + 1)
            .Select(i => (CampoDinamico?)new CampoDinamico(null, $"Campo {i}", TiposDatoCampo.Texto))
            .ToArray();

        Assert.True(Normalizar(campos).Errores.ContainsKey("campos"));
    }

    [Fact]
    public void Normalizar_ErroresPorCampoConSuPosicion()
    {
        var (_, errores) = Normalizar(
            new CampoDinamico(null, "Presión", TiposDatoCampo.Numero),
            new CampoDinamico(null, "PA", TiposDatoCampo.Numero),
            new CampoDinamico("Presion!", "Presión 2", "moneda"),
            new CampoDinamico(null, "Presión", TiposDatoCampo.Texto),
            null);

        Assert.Equal(["Escribe la etiqueta visible (al menos 3 caracteres)."], errores["campos[1].etiqueta"]);
        Assert.True(errores.ContainsKey("campos[2].nombre"));
        Assert.True(errores.ContainsKey("campos[2].tipoDato"));
        Assert.Equal(["Ya hay un campo con ese nombre."], errores["campos[3].nombre"]);
        Assert.Equal(["El campo está vacío."], errores["campos[4]"]);
        Assert.False(errores.ContainsKey("campos[0].etiqueta"));
    }

    [Fact]
    public void Seleccion_ExigeDosOpcionesDistintas()
    {
        var (_, errores) = Normalizar(new CampoDinamico(null, "Resultado", TiposDatoCampo.Seleccion, Opciones: ["Pasa", " pasa ", " "]));

        Assert.Equal(["Escribe al menos dos opciones."], errores["campos[0].opciones"]);
    }

    [Fact]
    public void Seleccion_ConservaLasOpcionesLimpias()
    {
        var (campos, errores) = Normalizar(new CampoDinamico(null, "Resultado", TiposDatoCampo.Seleccion, Opciones: [" Pasa ", "No pasa", "pasa"]));

        Assert.Empty(errores);
        Assert.Equal(["Pasa", "No pasa"], campos[0].Opciones!);
    }

    [Fact]
    public void OpcionesYUnidad_SoloSeGuardanDondeAplican()
    {
        var (campos, _) = Normalizar(
            new CampoDinamico(null, "Observación", TiposDatoCampo.Texto, Opciones: ["a", "b"], Unidad: "mmHg"),
            new CampoDinamico(null, "Frecuencia", TiposDatoCampo.Numero, Unidad: "lpm"));

        Assert.Equal((null, null), (campos[0].Opciones, campos[0].Unidad));
        Assert.Equal("lpm", campos[1].Unidad);
    }

    [Fact]
    public void CampoDinamico_IgualdadPorContenidoDeLasOpciones()
    {
        var a = new CampoDinamico("resultado", "Resultado", TiposDatoCampo.Seleccion, false, ["Pasa", "No pasa"]);
        var b = new CampoDinamico("resultado", "Resultado", TiposDatoCampo.Seleccion, false, new List<string> { "Pasa", "No pasa" });

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Opciones = ["Pasa"] });
    }
}
