using Sitbio.Domain.Seguridad;

namespace Sitbio.Domain.Tests.Seguridad;

public class SesionTokenTests
{
    private static readonly DateTimeOffset Emision = new(2026, 10, 29, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Crear_ConDatosValidos_ConservaLasFechas()
    {
        var sesion = new SesionToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), Emision, Emision.AddMinutes(30));

        Assert.Equal(Emision.AddMinutes(30), sesion.FechaExpiracion);
        Assert.Null(sesion.FechaRevocacion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Crear_ExpiracionNoPosteriorALaEmision_LanzaExcepcion(int minutos)
    {
        Assert.Throws<ArgumentException>(() =>
            new SesionToken(Guid.NewGuid(), Guid.NewGuid(), "jti-1", Emision, Emision.AddMinutes(minutos)));
    }

    [Fact]
    public void Crear_JtiVacioOMuyLargo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => new SesionToken(Guid.NewGuid(), Guid.NewGuid(), " ", Emision, Emision.AddMinutes(30)));
        Assert.Throws<ArgumentException>(() =>
            new SesionToken(Guid.NewGuid(), Guid.NewGuid(), new string('a', SesionToken.LongitudMaximaJti + 1), Emision, Emision.AddMinutes(30)));
    }

    [Fact]
    public void Crear_SinUsuario_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => new SesionToken(Guid.NewGuid(), Guid.Empty, "jti-1", Emision, Emision.AddMinutes(30)));
    }
}
