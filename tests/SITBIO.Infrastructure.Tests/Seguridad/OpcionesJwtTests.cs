using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Seguridad;

public class OpcionesJwtTests
{
    private static OpcionesJwt Validas() => new()
    {
        Issuer = "sitbio-api",
        Audience = "sitbio-desktop",
        Clave = new string('k', OpcionesJwt.BytesMinimosClave),
        MinutosVigencia = 30,
    };

    [Fact]
    public void Validar_ConfiguracionCorrecta_NoLanza()
    {
        Validas().Validar();
    }

    [Fact]
    public void Validar_ClaveCorta_Lanza()
    {
        var opciones = Validas();
        opciones.Clave = new string('k', OpcionesJwt.BytesMinimosClave - 1);

        Assert.Throws<InvalidOperationException>(opciones.Validar);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Validar_VigenciaFueraDeRango_Lanza(int minutos)
    {
        var opciones = Validas();
        opciones.MinutosVigencia = minutos;

        Assert.Throws<InvalidOperationException>(opciones.Validar);
    }

    [Fact]
    public void Validar_SinAudiencia_Lanza()
    {
        var opciones = Validas();
        opciones.Audience = " ";

        Assert.Throws<InvalidOperationException>(opciones.Validar);
    }
}
