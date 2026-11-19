using Sitbio.Infrastructure.Analitica;

namespace Sitbio.Infrastructure.Tests.Analitica;

/// <summary>I-04: la salida del lote se registra sin el usuario ni la contraseña de MongoDB (SEC-008).</summary>
public class EjecutorAnaliticaPythonTests
{
    [Theory]
    [InlineData("error en mongodb://raiz:secreta@[::1]:27017/?authSource=admin", "error en mongodb://***@[::1]:27017/?authSource=admin")]
    [InlineData("mongodb+srv://u:p@cluster.net/db", "mongodb+srv://***@cluster.net/db")]
    [InlineData("2 documento(s) escritos", "2 documento(s) escritos")]
    public void Ocultar_QuitaLasCredencialesDeLaCadena(string texto, string esperado) =>
        Assert.Equal(esperado, EjecutorAnaliticaPython.Ocultar(texto));
}
