using System.IO;
using System.Text.RegularExpressions;

namespace Sitbio.Desktop.Tests.Views;

/// <summary>
/// Un ContentControl con una plantilla dibuja la plantilla aunque el contenido sea null: sin Visibility, un aviso vacío
/// aparece como la barra roja con solo el ícono (regresión de la pestaña Mantenimientos en la v1.1.1).
/// </summary>
public partial class AvisosVaciosTests
{
    [Fact]
    public void TodoContentControlConPlantilla_SeOcultaSinContenido()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "SITBIO.sln")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        var vistas = Path.Combine(raiz.FullName, "src", "SITBIO.Desktop", "Views");
        var sinVisibilidad = Directory.GetFiles(vistas, "*.xaml")
            .SelectMany(a => ConPlantilla().Matches(File.ReadAllText(a))
                .Where(m => !m.Value.Contains("Visibility=", StringComparison.Ordinal))
                .Select(m => $"{Path.GetFileName(a)}: {m.Value[..Math.Min(90, m.Value.Length)]}"))
            .ToList();

        Assert.Empty(sinVisibilidad);
    }

    [GeneratedRegex(@"<ContentControl\b[^>]*ContentTemplate=""\{StaticResource [A-Za-z.]+\}""[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ConPlantilla();
}
