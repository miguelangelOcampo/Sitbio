using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>E-10: ícono por tipo de equipo (prototipo components/ui.tsx, EqIcon).</summary>
public class IconosTipoEquipoTests
{
    [Theory]
    [InlineData("Bomba de infusión", "Icono.Tipo.BombaInfusion")]
    [InlineData("BOMBA DE JERINGA", "Icono.Tipo.BombaInfusion")]
    [InlineData("Monitor de signos vitales", "Icono.Tipo.Monitor")]
    [InlineData("Monitor multiparámetro", "Icono.Tipo.Monitor")]
    [InlineData("Pulsioxímetro", "Icono.Tipo.Monitor")]
    [InlineData("Desfibrilador", "Icono.Tipo.Desfibrilador")]
    [InlineData("DEA", "Icono.Tipo.Desfibrilador")]
    [InlineData("Ventilador mecánico", "Icono.Tipo.Ventilador")]
    [InlineData("Electrocardiógrafo", "Icono.Tipo.Electrocardiografo")]
    [InlineData("Monitor Holter", "Icono.Tipo.Electrocardiografo")]
    [InlineData("ECG de 12 canales", "Icono.Tipo.Electrocardiografo")]
    [InlineData("Autoclave", "Icono.Tipo.Autoclave")]
    [InlineData("Equipo de esterilización", "Icono.Tipo.Autoclave")]
    [InlineData("Balanza neonatal", "Icono.Tipo.Balanza")]
    [InlineData("Báscula pesa-bebé", "Icono.Tipo.Balanza")]
    [InlineData("Electrobisturí", "Icono.Tipo.Electrobisturi")]
    [InlineData("Tensiómetro", "Icono.Tipo.Tensiometro")]
    [InlineData("Esfigmomanómetro aneroide", "Icono.Tipo.Tensiometro")]
    [InlineData("Estetoscopio", "Icono.Tipo.Estetoscopio")]
    [InlineData("Fonendoscopio", "Icono.Tipo.Estetoscopio")]
    public void ReconoceElTipoPorPalabrasClave_SinTildesNiMayusculas(string tipo, string clave) =>
        Assert.Equal(clave, IconosTipoEquipo.Clave(tipo));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Lámpara cielítica")]
    [InlineData("Idea de equipo")]
    public void SinCoincidencia_UsaElIconoGenerico(string? tipo) =>
        Assert.Equal(IconosTipoEquipo.Generico, IconosTipoEquipo.Clave(tipo));

    [Fact]
    public void LasFilasLaHojaDeVidaYLaBusquedaUsanElIconoDelTipo()
    {
        var equipo = EquiposPrueba.Equipo();

        Assert.Equal("Icono.Tipo.Monitor", new FilaEquipo(equipo, EquiposPrueba.Hoy).IconoTipo);
    }

    /// <summary>Cada clave existe en Themes/Icons.xaml, es una geometría válida y cabe en la cuadrícula de 24 × 24.</summary>
    [Fact]
    public void CadaClaveEsUnaGeometriaDeIconsXaml()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "SITBIO.sln")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        var xaml = File.ReadAllText(Path.Combine(raiz.FullName, "src", "SITBIO.Desktop", "Themes", "Icons.xaml"));
        var limites = new Dictionary<string, Rect>();
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                var recursos = (ResourceDictionary)XamlReader.Parse(xaml);
                foreach (var clave in IconosTipoEquipo.Claves)
                {
                    limites[clave] = Assert.IsAssignableFrom<Geometry>(recursos[clave]).Bounds;
                }
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();

        Assert.Null(error);
        Assert.Equal(11, limites.Count);
        Assert.All(limites, l => Assert.True(
            !l.Value.IsEmpty && l.Value.Left >= 0 && l.Value.Top >= 0 && l.Value.Right <= 24 && l.Value.Bottom <= 24,
            $"{l.Key}: {l.Value}"));
    }
}
