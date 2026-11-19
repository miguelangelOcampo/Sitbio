using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// H-01: íconos del submenú, de los botones y de las acciones como en el prototipo. Cada opción del menú de cada rol
/// tiene su ícono, y todo «Icono.*» que usan las vistas existe en Themes/Icons.xaml (un StaticResource que falta
/// cierra el cliente al abrir la pantalla).
/// </summary>
public partial class IconosAccionesTests
{
    private static readonly string[] CodigosRol = ["AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-06", "AC-07"];

    public static TheoryData<string> Roles => new(CodigosRol);

    [Theory]
    [MemberData(nameof(Roles))]
    public void CadaOpcionDelSubmenu_TieneSuIcono(string rol)
    {
        var opciones = MenuPorRol.Para(rol).SelectMany(m => m.Opciones).ToList();

        Assert.NotEmpty(opciones);
        Assert.All(opciones, o => Assert.False(string.IsNullOrEmpty(new OpcionViewModel(o).ClaveIcono), $"{rol}: {o.Etiqueta}"));
    }

    [Theory]
    [InlineData("UI-012", "Mis órdenes", "Icono.ListaCheck")]
    [InlineData("UI-012", "Órdenes de trabajo", "Icono.Portapapeles")]
    [InlineData("UI-015", "Calibraciones", "Icono.Regla")]
    [InlineData("UI-015", "Mantenimientos", "Icono.Calendario")]
    [InlineData("HIST", "Historial de intervenciones", "Icono.Historial")]
    [InlineData("UI-009", "Reportar falla", "Icono.Sirena")]
    public void ElIconoDependeDeLaPantallaYDeLaEtiqueta(string id, string etiqueta, string clave) =>
        Assert.Equal(clave, IconosMenu.Clave(new OpcionMenu(id, etiqueta)));

    /// <summary>Las claves de las vistas, de los estilos y del submenú existen y caben en la cuadrícula de 24 × 24.</summary>
    [Fact]
    public void TodoIconoUsado_ExisteEnIconsXaml_YCabeEnLaCuadricula()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "SITBIO.sln")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        var desktop = Path.Combine(raiz.FullName, "src", "SITBIO.Desktop");
        var usadas = Directory.GetFiles(Path.Combine(desktop, "Views"), "*.xaml")
            .Append(Path.Combine(desktop, "Themes", "Controls.xaml"))
            .SelectMany(a => UsoIcono().Matches(File.ReadAllText(a)).Select(m => m.Groups[1].Value))
            .Concat(CodigosRol.SelectMany(r => MenuPorRol.Para(r).SelectMany(m => m.Opciones)).Select(o => IconosMenu.Clave(o)!))
            .ToHashSet();
        var xaml = File.ReadAllText(Path.Combine(desktop, "Themes", "Icons.xaml"));
        var limites = new Dictionary<string, Rect>();
        var faltan = new List<string>();
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                var recursos = (ResourceDictionary)XamlReader.Parse(xaml);
                foreach (var clave in usadas)
                {
                    if (recursos[clave] is Geometry geometria)
                    {
                        limites[clave] = geometria.Bounds;
                    }
                    else
                    {
                        faltan.Add(clave);
                    }
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
        Assert.Empty(faltan);
        Assert.Contains("Icono.Sirena", usadas);
        Assert.All(limites, l => Assert.True(
            !l.Value.IsEmpty && l.Value.Left >= 0 && l.Value.Top >= 0 && l.Value.Right <= 24 && l.Value.Bottom <= 24,
            $"{l.Key}: {l.Value}"));
    }

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource) (Icono\.[A-Za-z.]+)\}")]
    private static partial Regex UsoIcono();
}
