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

    [Fact]
    public void Revocar_LaPrimeraVezGuardaFechaYMotivo_YLaSegundaNoLosCambia()
    {
        var sesion = new SesionToken(Guid.NewGuid(), Guid.NewGuid(), "jti-1", Emision, Emision.AddMinutes(30));

        Assert.True(sesion.Revocar(Emision.AddMinutes(5), MotivosRevocacion.CierreDeSesion));
        Assert.False(sesion.Revocar(Emision.AddMinutes(9), MotivosRevocacion.Renovacion));

        Assert.True(sesion.EstaRevocada);
        Assert.Equal(Emision.AddMinutes(5), sesion.FechaRevocacion);
        Assert.Equal(MotivosRevocacion.CierreDeSesion, sesion.MotivoRevocacion);
    }

    [Fact]
    public void Revocar_SinMotivoOMuyLargo_LanzaExcepcion()
    {
        var sesion = new SesionToken(Guid.NewGuid(), Guid.NewGuid(), "jti-1", Emision, Emision.AddMinutes(30));

        Assert.Throws<ArgumentException>(() => sesion.Revocar(Emision, " "));
        Assert.Throws<ArgumentException>(() => sesion.Revocar(Emision, new string('m', SesionToken.LongitudMaximaMotivo + 1)));
        Assert.False(sesion.EstaRevocada);
    }

    [Theory]
    [InlineData(29, false, true)]
    [InlineData(30, false, false)]
    [InlineData(10, true, false)]
    public void EstaVigente_SinRevocarYAntesDeExpirar(int minutos, bool revocada, bool vigente)
    {
        var sesion = new SesionToken(Guid.NewGuid(), Guid.NewGuid(), "jti-1", Emision, Emision.AddMinutes(30));
        if (revocada)
        {
            sesion.Revocar(Emision.AddMinutes(1), MotivosRevocacion.CierreDeSesion);
        }

        Assert.Equal(vigente, sesion.EstaVigente(Emision.AddMinutes(minutos)));
    }
}
