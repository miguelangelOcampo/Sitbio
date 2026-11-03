using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Seguridad;

public class GeneradorClavesTests
{
    private readonly GeneradorClaves _generador = new();

    [Fact]
    public void Generar_TieneLongitudYTodosLosTiposDeCaracter()
    {
        for (var i = 0; i < 50; i++)
        {
            var clave = _generador.Generar();

            Assert.Equal(GeneradorClaves.Longitud, clave.Length);
            Assert.Contains(clave, char.IsUpper);
            Assert.Contains(clave, char.IsLower);
            Assert.Contains(clave, char.IsDigit);
            Assert.Contains(clave, c => !char.IsLetterOrDigit(c));
            Assert.DoesNotContain(clave, c => "0O1lI".Contains(c));
        }
    }

    [Fact]
    public void Generar_NoRepiteClaves()
    {
        var claves = Enumerable.Range(0, 200).Select(_ => _generador.Generar()).ToHashSet();

        Assert.Equal(200, claves.Count);
    }
}
