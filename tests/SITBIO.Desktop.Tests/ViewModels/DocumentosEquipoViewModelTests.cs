using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>Pestaña «Documentos» de la hoja de vida UI-006 (HU-015; prototipo HojaVida.tsx).</summary>
public class DocumentosEquipoViewModelTests
{
    private const string Ruta = @"C:\Documentos\Manual MX450.pdf";
    private static readonly Guid EquipoId = Guid.NewGuid();
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();

    private readonly IServicioDocumentosEquipo _servicio = Substitute.For<IServicioDocumentosEquipo>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static DocumentoEquipo Documento(string nombre = "ficha.pdf", string tipo = "Ficha técnica", string mime = "application/pdf", long bytes = 402_000) =>
        new(Guid.NewGuid(), EquipoId, tipo, nombre, mime, bytes, new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero), "Laura Gómez");

    private async Task<DocumentosEquipoViewModel> AbrirAsync(bool puedeGestionar = true, params DocumentoEquipo[] documentos)
    {
        _servicio.ListarAsync(EquipoId, Arg.Any<CancellationToken>()).Returns(documentos);
        var vm = new DocumentosEquipoViewModel(_servicio, _dialogo, EquipoId, "HSV-EQ-0412", puedeGestionar);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private void ElegirArchivo(DocumentosEquipoViewModel vm, string ruta = Ruta, long bytes = 8)
    {
        _dialogo.ElegirArchivo(Arg.Any<string>(), Arg.Any<string>()).Returns(ruta);
        _dialogo.Tamano(ruta).Returns(bytes);
        _dialogo.Leer(ruta).Returns(Pdf);
        vm.ElegirArchivoCommand.Execute(null);
    }

    [Fact]
    public async Task Lista_MuestraTipoNombreTamanoFechaYQuienLoSubio()
    {
        var vm = await AbrirAsync(true, Documento(), Documento("placa.jpg", "Fotografía", "image/jpeg", 1_572_864));

        Assert.Equal("2 archivos", vm.Cantidad);
        Assert.Equal(["ficha.pdf", "placa.jpg"], vm.Filas!.Select(f => f.Nombre));
        Assert.Equal("Ficha técnica · 393 KB · 17/11/2026 · Laura Gómez", vm.Filas![0].Detalle);
        Assert.Equal("Fotografía · 1,5 MB · 17/11/2026 · Laura Gómez", vm.Filas![1].Detalle);
        Assert.Equal((false, true), (vm.Filas![0].EsImagen, vm.Filas![1].EsImagen));
        Assert.False(vm.SinDocumentos);
    }

    [Fact]
    public async Task SinDocumentos_MuestraElEstadoVacioConLaSugerenciaSoloParaElIngeniero()
    {
        var ingeniero = await AbrirAsync(true);
        var tecnico = await AbrirAsync(false);

        Assert.True(ingeniero.SinDocumentos);
        Assert.Equal("Adjunta el manual, la ficha técnica o los certificados de calibración.", ingeniero.TextoVacio);
        Assert.Equal(string.Empty, tecnico.TextoVacio);
    }

    [Fact]
    public async Task SinConexion_AvisaConMsg011YPermiteReintentar()
    {
        _servicio.ListarAsync(EquipoId, Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = new DocumentosEquipoViewModel(_servicio, _dialogo, EquipoId, "HSV-EQ-0412", true);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
    }

    // ---------- Agregar documento ----------

    [Fact]
    public async Task Agregar_SinTipoNiArchivo_MarcaLosCamposConMsg008YNoEnvia()
    {
        var vm = await AbrirAsync();
        vm.AbrirAgregarCommand.Execute(null);

        await vm.ConfirmarAgregarCommand.ExecuteAsync(null);

        Assert.True(vm.Agregando);
        Assert.Equal("Selecciona el tipo de documento.", vm.ErrorTipo);
        Assert.Equal("Selecciona un archivo.", vm.ErrorArchivo);
        Assert.Equal("MSG-008", vm.AvisoFormulario!.Codigo);
        await _servicio.DidNotReceiveWithAnyArgs().AdjuntarAsync(default, default!, default!, default!, default);
    }

    [Theory]
    [InlineData(@"C:\x\instalador.exe", 100, "Solo se aceptan PDF, JPG o PNG.")]
    [InlineData(@"C:\x\manual.pdf", (10 * 1024 * 1024) + 1, "El archivo supera 10 MB.")]
    [InlineData(@"C:\x\FICHA.PDF", 100, "Ya hay un documento con ese nombre en este equipo.")]
    public async Task Agregar_ArchivoNoPermitido_SeRechazaAntesDeEnviar(string ruta, long bytes, string error)
    {
        var vm = await AbrirAsync(true, Documento());
        vm.AbrirAgregarCommand.Execute(null);
        vm.Tipo = "Otro";
        ElegirArchivo(vm, ruta, bytes);

        await vm.ConfirmarAgregarCommand.ExecuteAsync(null);

        Assert.Equal(error, vm.ErrorArchivo);
        await _servicio.DidNotReceiveWithAnyArgs().AdjuntarAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task Agregar_Valido_EnviaElArchivoCierraYRecargaLaLista()
    {
        var vm = await AbrirAsync();
        var creado = Documento("Manual MX450.pdf", "Manual de usuario");
        _servicio.AdjuntarAsync(EquipoId, "Manual de usuario", "Manual MX450.pdf", Pdf, Arg.Any<CancellationToken>()).Returns(creado);
        vm.AbrirAgregarCommand.Execute(null);
        vm.Tipo = "Manual de usuario";
        ElegirArchivo(vm);
        Assert.Equal(("Manual MX450.pdf", "1 KB"), (vm.NombreArchivo, vm.TamanoArchivo));
        _servicio.ListarAsync(EquipoId, Arg.Any<CancellationToken>()).Returns([creado]);

        await vm.ConfirmarAgregarCommand.ExecuteAsync(null);

        Assert.False(vm.Agregando);
        Assert.Equal("Documento adjuntado", vm.Confirmacion!.Titulo);
        Assert.Equal(["Manual MX450.pdf"], vm.Filas!.Select(f => f.Nombre));
    }

    [Fact]
    public async Task Agregar_RechazoDeLaApi_MuestraElErrorDelCampoYElLimiteDelPlan()
    {
        var vm = await AbrirAsync();
        vm.AbrirAgregarCommand.Execute(null);
        vm.Tipo = "Otro";
        ElegirArchivo(vm);
        _servicio.AdjuntarAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(
            Task.FromException<DocumentoEquipo>(new ErrorApi(null, "Solo se aceptan PDF, JPG o PNG.", new Dictionary<string, string> { ["archivo"] = "Solo se aceptan PDF, JPG o PNG." })));

        await vm.ConfirmarAgregarCommand.ExecuteAsync(null);

        Assert.True(vm.Agregando);
        Assert.Equal("Solo se aceptan PDF, JPG o PNG.", vm.ErrorArchivo);

        _servicio.AdjuntarAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(
            Task.FromException<DocumentoEquipo>(new ErrorApi("RN-009", "Tu institución alcanzó el máximo de almacenamiento del plan Consultorio (5 GB).", new Dictionary<string, string>())));

        await vm.ConfirmarAgregarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-019", vm.AvisoFormulario!.Codigo);
        Assert.Null(vm.ErrorArchivo);
    }

    [Fact]
    public async Task SinPermiso_NoAbreAgregarNiDesactivar()
    {
        var vm = await AbrirAsync(false, Documento());

        vm.AbrirAgregarCommand.Execute(null);
        vm.PedirDesactivarCommand.Execute(vm.Filas![0]);

        Assert.False(vm.Agregando);
        Assert.Null(vm.PorDesactivar);
    }

    // ---------- Descargar ----------

    [Fact]
    public async Task Descargar_GuardaElArchivoDondeElUsuarioElige()
    {
        var documento = Documento();
        var vm = await AbrirAsync(false, documento);
        _dialogo.ElegirDestino("ficha.pdf").Returns(@"C:\Descargas\ficha.pdf");
        _servicio.DescargarAsync(EquipoId, documento.Id, Arg.Any<CancellationToken>()).Returns(new ArchivoDescargado(Pdf, "ficha.pdf"));

        await vm.DescargarCommand.ExecuteAsync(vm.Filas![0]);

        _dialogo.Received(1).Guardar(@"C:\Descargas\ficha.pdf", Pdf);
        Assert.Equal("Documento descargado", vm.Confirmacion!.Titulo);
    }

    [Fact]
    public async Task Descargar_Cancelado_NoPideNadaALaApi()
    {
        var vm = await AbrirAsync(false, Documento());
        _dialogo.ElegirDestino(Arg.Any<string>()).Returns((string?)null);

        await vm.DescargarCommand.ExecuteAsync(vm.Filas![0]);

        await _servicio.DidNotReceiveWithAnyArgs().DescargarAsync(default, default, default);
    }

    // ---------- Desactivar ----------

    [Fact]
    public async Task Desactivar_PideConfirmacionYAlConfirmarLoQuitaDeLaLista()
    {
        var documento = Documento();
        var vm = await AbrirAsync(true, documento);

        vm.PedirDesactivarCommand.Execute(vm.Filas![0]);

        Assert.Equal("«ficha.pdf» dejará de mostrarse en la hoja de vida de HSV-EQ-0412. El archivo se conserva y la acción queda en la bitácora de auditoría.", vm.TextoDesactivar);
        await _servicio.DidNotReceiveWithAnyArgs().DesactivarAsync(default, default, default);

        _servicio.ListarAsync(EquipoId, Arg.Any<CancellationToken>()).Returns([]);
        await vm.ConfirmarDesactivarCommand.ExecuteAsync(null);

        await _servicio.Received(1).DesactivarAsync(EquipoId, documento.Id, Arg.Any<CancellationToken>());
        Assert.Null(vm.PorDesactivar);
        Assert.Equal("Documento desactivado", vm.Confirmacion!.Titulo);
        Assert.True(vm.SinDocumentos);
    }

    [Fact]
    public async Task Desactivar_Cancelado_NoLlamaALaApi()
    {
        var vm = await AbrirAsync(true, Documento());

        vm.PedirDesactivarCommand.Execute(vm.Filas![0]);
        vm.CancelarDesactivarCommand.Execute(null);

        Assert.Null(vm.PorDesactivar);
        await _servicio.DidNotReceiveWithAnyArgs().DesactivarAsync(default, default, default);
    }

    // ---------- Integración con la hoja de vida ----------

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, "BAJA", false)]
    public async Task HojaDeVida_PestanaDocumentos_CargaAlAbrirlaYSoloAc02ConEquipoActivoGestiona(bool puedeEditar, string? estado, bool gestiona)
    {
        var equipos = Substitute.For<IServicioEquipos>();
        var equipo = estado == "BAJA" ? Equipo(estado: "BAJA", baja: DateTimeOffset.Now) : Equipo();
        equipos.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        _servicio.ListarAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns([Documento()]);
        var detalle = new DetalleEquipoViewModel(equipos, new Reloj(), equipo.Id, equipo.PlacaInventario, () => { }, puedeEditar, null, _servicio, _dialogo);
        await detalle.CargarCommand.ExecuteAsync(null);
        Assert.True(detalle.TieneDocumentos);
        Assert.Null(detalle.Documentos);

        detalle.EnDocumentos = true;

        Assert.False(detalle.EnResumen);
        Assert.Equal(gestiona, detalle.Documentos!.PuedeGestionar);
        Assert.Single(detalle.Documentos.Filas!);
    }

    [Fact]
    public void HojaDeVida_SinServicioDeDocumentos_NoMuestraLaPestana()
    {
        var detalle = new DetalleEquipoViewModel(Substitute.For<IServicioEquipos>(), new Reloj(), Guid.NewGuid(), "HSV-EQ-0412", () => { });

        Assert.False(detalle.TieneDocumentos);
    }

    [Theory]
    [InlineData(1, "1 KB")]
    [InlineData(402_000, "393 KB")]
    [InlineData(1_048_576, "1,0 MB")]
    public void Tamano_ComoElPrototipo(long bytes, string texto) => Assert.Equal(texto, FilaDocumento.Tamano(bytes));
}
