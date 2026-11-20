using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Data;
using Sitbio.Desktop.Controls;

namespace Sitbio.Desktop.Tests.Controls;

/// <summary>I-07: calendario en los campos de fecha, sin perder la escritura manual «dd/mm/aaaa».</summary>
public class CampoFechaTests
{
    private sealed class Formulario : INotifyPropertyChanged
    {
        private string fecha = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Fecha
        {
            get => fecha;
            set
            {
                fecha = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fecha)));
            }
        }
    }

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                accion();
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
    }

    [Theory]
    [InlineData("05/03/2026", 2026, 3, 5)]
    [InlineData("5/3/2026", 2026, 3, 5)]
    [InlineData(" 31/12/2025 ", 2025, 12, 31)]
    public void Interpretar_AceptaElFormatoColombiano(string texto, int anio, int mes, int dia) =>
        Assert.Equal(new DateTime(anio, mes, dia), CampoFecha.Interpretar(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-03-05")]
    [InlineData("31/02/2026")]
    [InlineData("03/15/2026")]
    public void Interpretar_SinFechaValida_DevuelveNull(string? texto) => Assert.Null(CampoFecha.Interpretar(texto));

    [Fact]
    public void Formatear_UsaDiaMesAnio() => Assert.Equal("07/01/2026", CampoFecha.Formatear(new DateTime(2026, 1, 7)));

    [Fact]
    public void ElegirUnDia_EscribeLaFechaEnElViewModelYCierra() => EnSta(() =>
    {
        var formulario = new Formulario();

        // Algunas vistas actualizan al perder el foco: elegir en el calendario debe llegar igual al ViewModel.
        var campo = new CampoFecha { DataContext = formulario, Abierto = true };
        campo.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding(nameof(Formulario.Fecha)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });

        campo.Elegir(new DateTime(2026, 11, 3));

        Assert.Equal("03/11/2026", campo.Text);
        Assert.Equal("03/11/2026", formulario.Fecha);
        Assert.False(campo.Abierto);
    });

    [Fact]
    public void SeSigueEscribiendoAMano() => EnSta(() =>
    {
        var formulario = new Formulario();
        var campo = new CampoFecha { DataContext = formulario };
        campo.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding(nameof(Formulario.Fecha)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

        Assert.False(campo.IsReadOnly);
        campo.Text = "15/08/2026";

        Assert.Equal("15/08/2026", formulario.Fecha);
    });

    [Fact]
    public void LaPlantillaTieneCalendarioDelTemaEnEspanol()
    {
        var tema = File.ReadAllText(Path.Combine(Raiz(), "src", "SITBIO.Desktop", "Themes", "Controls.xaml"));

        Assert.Matches(new Regex(@"<Style TargetType=""controls:CampoFecha"" BasedOn=""\{StaticResource FieldInput\}"">"), tema);
        Assert.Contains(@"<Calendar x:Name=""PART_Calendario"" Style=""{StaticResource Calendario}""", tema);
        Assert.Contains(@"<Setter Property=""Language"" Value=""es-CO"" />", tema);

        // Los colores del calendario salen de los pinceles del tema (claro y oscuro), no de colores fijos.
        var inicio = tema.IndexOf("<Style x:Key=\"CalendarioBotonNav\"", StringComparison.Ordinal);
        var fin = tema.IndexOf("<Style TargetType=\"controls:CampoFecha\"", StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(Background|Foreground|BorderBrush|Fill)=""#"), tema[inicio..fin]);
    }

    [Fact]
    public void TodosLosCamposDeFechaUsanElCalendario()
    {
        var vistas = Directory.GetFiles(Path.Combine(Raiz(), "src", "SITBIO.Desktop", "Views"), "*.xaml");
        var sinCalendario = new List<string>();
        var conCalendario = 0;
        foreach (var vista in vistas)
        {
            var texto = File.ReadAllText(vista);
            conCalendario += Regex.Matches(texto, "<controls:CampoFecha ").Count;
            sinCalendario.AddRange(Regex.Matches(texto, @"<TextBox [^>]*AutomationProperties\.Name=""[^""]*dd/mm/aaaa[^""]*""")
                .Select(m => Path.GetFileName(vista) + ": " + m.Value));
        }

        Assert.Empty(sinCalendario);
        Assert.Equal(17, conCalendario);
    }

    private static string Raiz([CallerFilePath] string archivo = "")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SITBIO.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivo)!, "..", "..", ".."));
    }
}
