using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// UI-013 Intervención (HU-005, HU-006): el formulario se arma con los campos dinámicos de la versión activa
/// según su tipo de dato, se valida en el cliente, envía cada valor con su tipo y muestra junto a cada campo
/// lo que rechace la API.
/// </summary>
public class IntervencionViewModelTests
{
    private static readonly CampoDinamico Goteo = new("tasa_goteo", "Tasa de goteo", TiposDatoCampo.NumeroDecimal, true, null, "ml/h");
    private static readonly CampoDinamico Ciclos = new("ciclos", "Ciclos", TiposDatoCampo.Numero, false);
    private static readonly CampoDinamico Resultado = new("resultado", "Resultado", TiposDatoCampo.Seleccion, true, ["Pasa", "No pasa"]);
    private static readonly CampoDinamico Calibrado = new("calibrado", "Calibrado", TiposDatoCampo.Booleano, false);
    private static readonly CampoDinamico Vence = new("vence", "Vence", TiposDatoCampo.Fecha, false);
    private static readonly CampoDinamico Notas = new("notas", "Notas", TiposDatoCampo.Texto, false);

    private readonly IServicioOrdenes _ordenes = Substitute.For<IServicioOrdenes>();
    private readonly IServicioEquipos _equipos = Substitute.For<IServicioEquipos>();
    private readonly IServicioConfiguracionCampos _configuracion = Substitute.For<IServicioConfiguracionCampos>();
    private readonly Equipo _equipo = EquiposPrueba.Equipo();
    private readonly List<Orden?> _vueltas = [];
    private DatosIntervencion? _enviada;

    private static Intervencion Guardada(Orden orden, bool firmada = false, int version = 3) => new(
        Guid.NewGuid(), orden.Id, "Sensor descalibrado", "Calibración", [new Repuesto("Sensor", 2, 15000m)], 40000m, 70000m, 90, "Operativo",
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"tasa_goteo": 25.5, "ciclos": 3, "resultado": "Pasa", "calibrado": true, "vence": "2027-05-01", "notas": "Sin novedad"}""")!,
        version, DateTimeOffset.UtcNow, firmada);

    private async Task<IntervencionViewModel> AbrirAsync(Orden orden, bool editable = true, Intervencion? guardada = null, int? version = 3)
    {
        _equipos.ObtenerAsync(orden.EquipoId, Arg.Any<CancellationToken>()).Returns(_equipo);
        _configuracion.ObtenerAsync(_equipo.TipoEquipoId, Arg.Any<CancellationToken>()).Returns(
            new ConfiguracionCampos(_equipo.TipoEquipoId, "Bomba", version, DateTimeOffset.UtcNow, [Goteo, Ciclos, Resultado, Calibrado, Vence, Notas], []));
        _ordenes.ObtenerIntervencionAsync(orden.Id, Arg.Any<CancellationToken>()).Returns(guardada);
        _ordenes.RegistrarIntervencionAsync(orden.Id, Arg.Any<DatosIntervencion>(), Arg.Any<CancellationToken>()).Returns(llamada =>
        {
            _enviada = llamada.ArgAt<DatosIntervencion>(1);
            return Guardada(orden);
        });
        var vm = new IntervencionViewModel(_ordenes, _equipos, _configuracion, orden, editable, _vueltas.Add);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    private static CampoIntervencion Campo(IntervencionViewModel vm, string nombre) => vm.Campos.Single(c => c.Campo.Nombre == nombre);

    private static void Completar(IntervencionViewModel vm)
    {
        vm.Diagnostico = "  Sensor descalibrado ";
        vm.AccionesRealizadas = "Calibración del sensor";
        Campo(vm, "tasa_goteo").Texto = "25,5";
        Campo(vm, "resultado").Opcion = "Pasa";
    }

    [Fact]
    public async Task Cargar_ArmaUnControlPorTipoDeDato()
    {
        var vm = await AbrirAsync(OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion));

        Assert.Equal(["tasa_goteo", "ciclos", "resultado", "calibrado", "vence", "notas"], vm.Campos.Select(c => c.Campo.Nombre));
        Assert.Equal((int?)3, vm.VersionCampos);
        Assert.Equal("Tasa de goteo (ml/h)", Campo(vm, "tasa_goteo").Etiqueta);
        Assert.True(Campo(vm, "calibrado").EsBooleano);
        Assert.True(Campo(vm, "resultado").EsSeleccion);
        Assert.Equal(["Pasa", "No pasa"], Campo(vm, "resultado").Opciones);
        Assert.All(new[] { "tasa_goteo", "ciclos", "vence", "notas" }, n => Assert.True(Campo(vm, n).EsTextoLibre));
        Assert.Equal("dd/mm/aaaa", Campo(vm, "vence").Ayuda);
        Assert.True(vm.SinRepuestos);
        Assert.Null(vm.Nota);
    }

    [Fact]
    public async Task Guardar_EnviaCadaValorConSuTipo_YVuelveALaLista()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion);
        var vm = await AbrirAsync(orden);
        Completar(vm);
        Campo(vm, "ciclos").Texto = "12";
        Campo(vm, "calibrado").No = true;
        Campo(vm, "vence").Texto = "01/05/2027";
        vm.AgregarRepuestoCommand.Execute(null);
        vm.Repuestos[0].Nombre = "Sensor de presión";
        vm.Repuestos[0].Cantidad = "2";
        vm.Repuestos[0].CostoUnitario = "15.000";
        vm.CostoManoObra = "40000";
        vm.TiempoAtencion = "90";
        vm.EstadoSalida = "Operativo";

        Assert.Contains("70.000", vm.CostoTotal);
        await vm.GuardarCommand.ExecuteAsync(null);

        var datos = _enviada!;
        Assert.Equal(("Sensor descalibrado", "Calibración del sensor", (decimal?)40000m, (int?)90, "Operativo"),
            (datos.Diagnostico, datos.AccionesRealizadas, datos.CostoManoObra, datos.TiempoAtencionMin, datos.EstadoEquipoSalida));
        Assert.Equal([new Repuesto("Sensor de presión", 2, 15000m)], datos.Repuestos);
        Assert.Equal(25.5m, datos.CamposDinamicos["tasa_goteo"]);
        Assert.Equal(12L, datos.CamposDinamicos["ciclos"]);
        Assert.Equal("Pasa", datos.CamposDinamicos["resultado"]);
        Assert.Equal(false, datos.CamposDinamicos["calibrado"]);
        Assert.Equal("2027-05-01", datos.CamposDinamicos["vence"]);
        Assert.False(datos.CamposDinamicos.ContainsKey("notas"));
        Assert.Equal([orden with { TieneIntervencion = true }], _vueltas);
    }

    [Fact]
    public async Task ValidacionDelCliente_MarcaCadaCampo_YNoLlamaALaApi()
    {
        var vm = await AbrirAsync(OrdenesViewModelTests.Orden(EstadosOrden.Pausada));
        Campo(vm, "ciclos").Texto = "2,5";
        Campo(vm, "vence").Texto = "2027-05-01";
        vm.TiempoAtencion = "una hora";
        vm.CostoManoObra = "-10";
        vm.AgregarRepuestoCommand.Execute(null);
        vm.Repuestos[0].Cantidad = "0";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal((IntervencionViewModel.Obligatorio, IntervencionViewModel.Obligatorio), (vm.ErrorDiagnostico, vm.ErrorAcciones));
        Assert.Equal(IntervencionViewModel.Obligatorio, Campo(vm, "tasa_goteo").Error);
        Assert.Equal(IntervencionViewModel.Obligatorio, Campo(vm, "resultado").Error);
        Assert.Equal("Escribe un número entero.", Campo(vm, "ciclos").Error);
        Assert.Equal("Escribe la fecha como dd/mm/aaaa.", Campo(vm, "vence").Error);
        Assert.NotNull(vm.ErrorTiempo);
        Assert.NotNull(vm.ErrorManoObra);
        Assert.Equal("Escribe el nombre del repuesto.", vm.Repuestos[0].Error);
        Assert.Equal("Revisa la intervención", vm.Aviso!.Titulo);
        await _ordenes.DidNotReceiveWithAnyArgs().RegistrarIntervencionAsync(default, default!, default);
        Assert.Empty(_vueltas);

        // Al corregir, el error del campo desaparece.
        Campo(vm, "ciclos").Texto = "3";
        Assert.Null(Campo(vm, "ciclos").Error);
    }

    [Fact]
    public async Task ErroresDeLaApi_SeMuestranJuntoASuCampo()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion);
        var vm = await AbrirAsync(orden);
        Completar(vm);
        _ordenes.RegistrarIntervencionAsync(orden.Id, Arg.Any<DatosIntervencion>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorApi(null, "Datos inválidos",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["camposDinamicos.tasa_goteo"] = "«Tasa de goteo» debe ser un número.",
                ["diagnostico"] = "Máximo 4000 caracteres.",
                ["repuestos[0].cantidad"] = "La cantidad va de 1 a 10000.",
            }));

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("«Tasa de goteo» debe ser un número.", Campo(vm, "tasa_goteo").Error);
        Assert.Equal("Máximo 4000 caracteres.", vm.ErrorDiagnostico);
        Assert.Equal("La cantidad va de 1 a 10000.", vm.ErrorRepuestos);
        Assert.Equal("Revisa la intervención", vm.Aviso!.Titulo);
        Assert.Empty(_vueltas);
    }

    [Fact]
    public async Task MongoSinRespuesta_AvisaQueLaOrdenNoCambio_YSePuedeReintentar()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion);
        var vm = await AbrirAsync(orden);
        Completar(vm);
        _ordenes.RegistrarIntervencionAsync(orden.Id, Arg.Any<DatosIntervencion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "No se pudo guardar la intervención porque el servidor de documentos no respondió. La orden no cambió; intenta de nuevo.", new Dictionary<string, string>()));

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("No se pudo guardar la intervención", vm.Aviso!.Titulo);
        Assert.Contains("La orden no cambió", vm.Aviso.Mensaje);
        Assert.True(vm.Editable);
        Assert.True(vm.GuardarCommand.CanExecute(null));
        Assert.Empty(_vueltas);
    }

    [Fact]
    public async Task Existente_SePrecargaConSusValores_YAvisaSiCambioLaVersion()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.Pausada, intervencion: true);
        var vm = await AbrirAsync(orden, guardada: Guardada(orden, version: 2), version: 3);

        Assert.Equal(("Sensor descalibrado", "Calibración", "40000", "90", "Operativo"), (vm.Diagnostico, vm.AccionesRealizadas, vm.CostoManoObra, vm.TiempoAtencion, vm.EstadoSalida));
        Assert.Equal(("Sensor", "2", "15000"), (vm.Repuestos[0].Nombre, vm.Repuestos[0].Cantidad, vm.Repuestos[0].CostoUnitario));
        Assert.Equal("25,5", Campo(vm, "tasa_goteo").Texto);
        Assert.Equal("3", Campo(vm, "ciclos").Texto);
        Assert.Equal("Pasa", Campo(vm, "resultado").Opcion);
        Assert.True(Campo(vm, "calibrado").Si);
        Assert.Equal("01/05/2027", Campo(vm, "vence").Texto);
        Assert.Equal("Sin novedad", Campo(vm, "notas").Texto);
        Assert.Contains("versión 2", vm.Nota);
    }

    [Fact]
    public async Task Firmada_QuedaEnSoloLectura_Rn004()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion, intervencion: true);
        var vm = await AbrirAsync(orden, guardada: Guardada(orden, firmada: true));

        Assert.False(vm.Editable);
        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Contains("RN-004", vm.Nota);
        Assert.All(vm.Campos, c => Assert.False(c.Editable));
    }

    [Fact]
    public async Task SinPermisoDeEdicion_SoloLectura()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.Cerrada, intervencion: true);
        var vm = await AbrirAsync(orden, editable: false, guardada: Guardada(orden));

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Equal("Estás viendo la intervención en solo lectura.", vm.Nota);

        vm.CancelarCommand.Execute(null);
        Assert.Equal([(Orden?)null], _vueltas);
    }

    [Fact]
    public async Task ErrorAlAbrir_PermiteReintentar()
    {
        var orden = OrdenesViewModelTests.Orden(EstadosOrden.EnEjecucion);
        _equipos.ObtenerAsync(orden.EquipoId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = new IntervencionViewModel(_ordenes, _equipos, _configuracion, orden, true, _vueltas.Add);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
    }

    [Theory]
    [InlineData("15.000", "15000")]
    [InlineData("$ 1.250.000", "1250000")]
    [InlineData("25,5", "25.5")]
    [InlineData("1500", "1500")]
    [InlineData("12.5", "12.5")]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void Decimal_AceptaPesosConPuntosDeMilesYComaDecimal(string texto, string? esperado)
    {
        Assert.Equal(esperado is null ? null : decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), IntervencionViewModel.Decimal(texto));
    }
}
