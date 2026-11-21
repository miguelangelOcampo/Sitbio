using System.Windows;
using System.Windows.Controls;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// J-03: «Exportar PDF» en Reportes e indicadores (UI-021) y en Analítica (UI-023). El cliente envía los filtros de la
/// pantalla y los gráficos capturados en PNG; guarda el archivo donde elija el usuario.
/// </summary>
public class ExportarPdfTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];

    private readonly IServicioReportes _reportes = Substitute.For<IServicioReportes>();
    private readonly IServicioAnalitica _analitica = Substitute.For<IServicioAnalitica>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static Indicadores Kpi() => new(
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20),
        new CumplimientoPreventivo(8, 6, 75), 6, 2880, new CostoPorEquipo(500000m, 20, 25000m), new FueraDeServicio(2, 20, 10), true);

    private async Task<ReportesViewModel> ReportesCargado()
    {
        _reportes.IndicadoresAsync(default, default, default).ReturnsForAnyArgs(Kpi());
        _reportes.SerieIndicadoresAsync(default, default).ReturnsForAnyArgs([]);
        var vm = new ReportesViewModel(_reportes, null, _dialogo);
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Reportes_EnviaElPeriodoConsultadoLosMesesYLosGraficos_YGuardaElPdf()
    {
        var vm = await ReportesCargado();
        vm.CapturarGraficos = () => [new GraficoCapturado("Mantenimientos por mes", Png)];
        vm.MesesSerie = 12;
        SolicitudPdfIndicadores? enviada = null;
        _reportes.IndicadoresPdfAsync(Arg.Do<SolicitudPdfIndicadores>(s => enviada = s), Arg.Any<CancellationToken>())
            .Returns(new ArchivoDescargado([1, 2], "indicadores-20261101-20261120.pdf"));
        _dialogo.ElegirDestino("indicadores-20261101-20261120.pdf").Returns(@"C:\Reportes\indicadores.pdf");

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), 12), (enviada!.Desde, enviada.Hasta, enviada.Meses));
        Assert.Equal(new GraficoPdf("Mantenimientos por mes", Convert.ToBase64String(Png)), enviada.Graficos.Single());
        _dialogo.Received(1).Guardar(@"C:\Reportes\indicadores.pdf", Arg.Is<byte[]>(b => b.Length == 2));
        Assert.Equal("Archivo guardado", vm.Confirmacion?.Titulo);
        Assert.False(vm.ExportandoPdf);
    }

    [Fact]
    public async Task Reportes_ErrorDeLaApi_O_Cancelar_NoGuarda()
    {
        var vm = await ReportesCargado();
        _reportes.IndicadoresPdfAsync(default!, default).ThrowsAsyncForAnyArgs(new ErrorApi("VAL-001", "Solo se aceptan imágenes PNG.", new Dictionary<string, string>()));

        await vm.ExportarPdfCommand.ExecuteAsync(null);
        Assert.Equal("No pudimos generar el PDF", vm.Aviso?.Titulo);

        _reportes.IndicadoresPdfAsync(default!, default).ReturnsForAnyArgs(new ArchivoDescargado([1], "x.pdf"));
        _dialogo.ElegirDestino(default!).ReturnsForAnyArgs((string?)null);
        await vm.ExportarPdfCommand.ExecuteAsync(null);

        _dialogo.DidNotReceiveWithAnyArgs().Guardar(default!, default!);
        Assert.Null(vm.Confirmacion);
        Assert.Null(vm.Aviso);
    }

    [Fact]
    public async Task Analitica_EnviaLosGruposYLosEjesElegidos()
    {
        var equipos = new List<EquipoAgrupado> { new(Guid.NewGuid(), "PL-1", 0, new Caracteristicas(2, 0.5, 1e6, 30), "Monitor"), new(Guid.NewGuid(), "PL-2", 1, new Caracteristicas(8, 0.5, 4e6, 90), "Monitor") };
        var grupos = new List<GrupoAnalitica> { new(0, 1, new Caracteristicas(2, 0.5, 1e6, 30), "Estables"), new(1, 1, new Caracteristicas(8, 0.5, 4e6, 90), "Muchas fallas") };
        _analitica.AgrupamientosAsync(default).ReturnsForAnyArgs(new Agrupamiento(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 2, grupos, equipos, [new AgrupacionAnalitica(2, 1, grupos, equipos)]));
        var vm = new AnaliticaViewModel(_analitica, null, _dialogo);
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        await vm.CargarCommand.ExecuteAsync(null);
        vm.EjeX = AnaliticaViewModel.OpcionesEje.Single(e => e.Clave == "tiempo");
        vm.CapturarGraficos = () => [new GraficoCapturado(vm.TituloGrafico, Png)];
        SolicitudPdfAnalitica? enviada = null;
        _analitica.AnaliticaPdfAsync(Arg.Do<SolicitudPdfAnalitica>(s => enviada = s), Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado([7], "analitica-2-grupos.pdf"));
        _dialogo.ElegirDestino("analitica-2-grupos.pdf").Returns(@"C:\Reportes\analitica.pdf");

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal((2, "tiempo", "costo"), (enviada!.Grupos, enviada.EjeX, enviada.EjeY));
        Assert.Equal("Costo confirmado frente a tiempo de atención", enviada.Graficos.Single().Titulo);
        _dialogo.Received(1).Guardar(@"C:\Reportes\analitica.pdf", Arg.Any<byte[]>());

        // Con un número de grupos que el último cálculo no tiene, no hay qué exportar.
        vm.GruposSolicitados = 5;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
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

    [Fact]
    public void Captura_SoloLosGraficosVisiblesConTitulo_ComoPngA2x() => EnSta(() =>
    {
        Grafico Linea(string titulo) => new()
        {
            Titulo = titulo,
            Width = 200,
            Height = 100,
            Etiquetas = ["a", "b"],
            Series = [new SerieGrafico("S", "Brush.Primary", [1, 2])],
        };
        var oculto = Linea("Oculto");
        oculto.Visibility = Visibility.Collapsed;
        var panel = new StackPanel { Children = { Linea("Mantenimientos por mes"), Linea(string.Empty), oculto } };
        var ventana = new Window { Content = panel, Width = 300, Height = 400, ShowInTaskbar = false, ShowActivated = false, Left = -2000, Top = -2000 };
        try
        {
            ventana.Show();
            ventana.UpdateLayout();

            var capturas = CapturaGraficos.Capturar(panel);

            var captura = Assert.Single(capturas);
            Assert.Equal("Mantenimientos por mes", captura.Titulo);
            Assert.Equal(Png[..8], captura.Png[..8]);
            var imagen = System.Windows.Media.Imaging.BitmapFrame.Create(new MemoryStream(captura.Png));
            Assert.Equal((400, 200), (imagen.PixelWidth, imagen.PixelHeight));
        }
        finally
        {
            ventana.Close();
        }
    });
}
