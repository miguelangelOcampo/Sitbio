using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-008 Importar inventario (HU-016; prototipo screens/Importacion.tsx).</summary>
public class ImportacionViewModelTests
{
    private const string Ruta = @"C:\Inventario\inventario_san_rafael.xlsx";
    private static readonly byte[] Libro = [0x50, 0x4B, 0x03, 0x04];

    private readonly IServicioImportacion _servicio = Substitute.For<IServicioImportacion>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static ResultadoImportacion Resultado(bool confirmada, int total, int validas, int cupo, params ErrorFilaImportacion[] errores)
    {
        var importables = Math.Min(validas, cupo);
        return new(confirmada, total, validas, cupo, importables, confirmada ? importables : 0, errores);
    }

    private ImportacionViewModel ConArchivo(string ruta = Ruta, long bytes = 48_000)
    {
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns(ruta);
        _dialogo.Tamano(ruta).Returns(bytes);
        _dialogo.Leer(ruta).Returns(Libro);
        var vm = new ImportacionViewModel(_servicio, _dialogo);
        vm.ElegirArchivoCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task Paso1_SinArchivoOConOtroFormato_NoLlamaALaApi()
    {
        var vacio = new ImportacionViewModel(_servicio, _dialogo);
        await vacio.RevisarCommand.ExecuteAsync(null);
        var txt = ConArchivo(@"C:\Inventario\notas.txt");
        var grande = ConArchivo(@"C:\Inventario\grande.csv", 6L * 1024 * 1024);
        await txt.RevisarCommand.ExecuteAsync(null);

        Assert.Equal("Selecciona el archivo con el inventario.", vacio.ErrorArchivo);
        Assert.Equal("Solo se aceptan archivos CSV o Excel (.xlsx).", txt.ErrorArchivo);
        Assert.Equal("El archivo supera 5 MB.", grande.ErrorArchivo);
        Assert.Equal(1, txt.Paso);
        await _servicio.DidNotReceiveWithAnyArgs().ProcesarAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Paso2_LaVistaPreviaMuestraConteosYErroresPorFila_SinGuardar()
    {
        _servicio.ProcesarAsync("inventario_san_rafael.xlsx", Libro, false, Arg.Any<CancellationToken>()).Returns(Resultado(false, 12, 9, 100,
            new(4, "PL-00123", "Placa de inventario", "Ya existe un equipo con la placa de inventario PL-00123 en tu institución."),
            new(6, "PL-00624", "Número de serie", "Este dato es obligatorio."),
            new(6, "PL-00624", "Vence registro", "Este dato es obligatorio."),
            new(8, "PL-00626", "Tipo de equipo", "El tipo de equipo «Incubadora» no existe en tu institución.")));
        var vm = ConArchivo();

        await vm.RevisarCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Paso);
        Assert.Equal((12, 9, 3, 9), (vm.TotalFilas, vm.FilasValidas, vm.FilasConError, vm.Importables));
        Assert.Equal([4, 6, 8], vm.FilasVisibles.Select(f => f.Fila));
        Assert.Equal(["Número de serie: Este dato es obligatorio.", "Vence registro: Este dato es obligatorio."], vm.FilasVisibles[1].Motivos);
        Assert.Equal("Continuar con 9 filas válidas", vm.TextoContinuar);
        Assert.Empty(vm.TextoSinCupo);
        await _servicio.DidNotReceive().ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paso2_ColumnasFaltantes_SeMuestranJuntoAlArchivo()
    {
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Revisa los datos.", new Dictionary<string, string> { ["archivo"] = "Faltan columnas de la plantilla: Marca." }));
        var vm = ConArchivo();

        await vm.RevisarCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Paso);
        Assert.Equal("Faltan columnas de la plantilla: Marca.", vm.ErrorArchivo);
        Assert.Null(vm.Aviso);
    }

    [Fact]
    public async Task RN_009_SinCupoParaTodas_AvisaCuantasNoSeImportan()
    {
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>())
            .Returns(Resultado(false, 5, 5, 2, new(4, "P-4", "Plan", "No se importa: supera el máximo."), new(5, "P-5", "Plan", "No se importa: supera el máximo.")));
        var vm = ConArchivo();

        await vm.RevisarCommand.ExecuteAsync(null);

        Assert.Equal((2, 3, 3), (vm.Importables, vm.SinCupo, vm.FilasOmitidas));
        Assert.Contains("solo admite 2 equipos más: 3 filas válidas no se importarán", vm.TextoSinCupo);
    }

    [Fact]
    public async Task SinFilasValidas_NoSePuedeContinuar()
    {
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>())
            .Returns(Resultado(false, 1, 0, 10, new ErrorFilaImportacion(2, null, "Marca", "Este dato es obligatorio.")));
        var vm = ConArchivo();
        await vm.RevisarCommand.ExecuteAsync(null);

        vm.ContinuarCommand.Execute(null);

        Assert.False(vm.PuedeContinuar);
        Assert.Equal(2, vm.Paso);
        Assert.Equal("—", vm.FilasVisibles[0].PlacaTexto);
    }

    [Fact]
    public async Task Paso3_Confirmar_ImportaYMuestraMsg015ConElInforme()
    {
        var error = new ErrorFilaImportacion(4, "PL-00123", "Placa de inventario", "Ya existe; revisa \"PL-00123\".");
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>()).Returns(Resultado(false, 3, 2, 10, error));
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>()).Returns(Resultado(true, 3, 2, 10, error));
        _dialogo.ElegirDestino("informe-importacion-inventario_san_rafael.csv").Returns(@"C:\Informes\informe.csv");
        var vm = ConArchivo();
        await vm.RevisarCommand.ExecuteAsync(null);
        vm.ContinuarCommand.Execute(null);

        await vm.ConfirmarCommand.ExecuteAsync(null);
        await vm.DescargarInformeCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Paso);
        Assert.True(vm.Terminado);
        Assert.Equal("2 equipos se agregaron al inventario en estado «Operativo».", vm.TextoTerminado);
        Assert.True(vm.Msg015Visible);
        Assert.Equal("Se importaron 2 equipos. 1 filas tienen errores; descargue el informe para corregirlas.", vm.TextoMsg015);
        _dialogo.Received(1).Guardar(@"C:\Informes\informe.csv", Arg.Is<byte[]>(b =>
            Encoding.UTF8.GetString(b).Contains("Fila;Placa de inventario;Columna;Motivo\r\n4;PL-00123;Placa de inventario;\"Ya existe; revisa \"\"PL-00123\"\".\"\r\n", StringComparison.Ordinal)));

        // Ya importado: no se puede confirmar otra vez ni volver atrás.
        await vm.ConfirmarCommand.ExecuteAsync(null);
        vm.AtrasCommand.Execute(null);
        await _servicio.Received(1).ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>());
        Assert.Equal(3, vm.Paso);
    }

    [Fact]
    public async Task SinConexionAlConfirmar_MuestraMsg011YNoTermina()
    {
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>()).Returns(Resultado(false, 1, 1, 10));
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = ConArchivo();
        await vm.RevisarCommand.ExecuteAsync(null);
        vm.ContinuarCommand.Execute(null);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.False(vm.Terminado);
        Assert.Equal("MSG-011", vm.Aviso?.Codigo);
    }

    [Fact]
    public async Task DescargarPlantilla_GuardaElArchivoDeLaApi()
    {
        var plantilla = new ArchivoDescargado([1, 2, 3], "plantilla-importacion-equipos.csv");
        _servicio.DescargarPlantillaAsync("csv", Arg.Any<CancellationToken>()).Returns(plantilla);
        _dialogo.ElegirDestino("plantilla-importacion-equipos.csv").Returns(@"C:\Plantillas\plantilla.csv");
        var vm = new ImportacionViewModel(_servicio, _dialogo);

        await vm.DescargarPlantillaCommand.ExecuteAsync("csv");

        _dialogo.Received(1).Guardar(@"C:\Plantillas\plantilla.csv", plantilla.Contenido);
        Assert.Equal("Plantilla descargada", vm.Confirmacion?.Titulo);
    }

    [Fact]
    public async Task ImportarOtroYVerInventario()
    {
        _servicio.ProcesarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Resultado(true, 1, 1, 10));
        var vm = ConArchivo();
        await vm.RevisarCommand.ExecuteAsync(null);
        vm.ContinuarCommand.Execute(null);
        await vm.ConfirmarCommand.ExecuteAsync(null);
        SolicitudNavegacion? solicitud = null;
        vm.NavegacionSolicitada += (_, s) => solicitud = s;

        vm.VerInventarioCommand.Execute(null);
        vm.ImportarOtroCommand.Execute(null);

        Assert.Equal("UI-005", solicitud?.IdOpcion);
        Assert.Equal((1, false, false), (vm.Paso, vm.HayArchivo, vm.Terminado));
    }
}
