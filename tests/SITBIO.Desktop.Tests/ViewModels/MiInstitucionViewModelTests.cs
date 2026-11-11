using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-019 Mi institución (HU-024): datos editables, plan y medidores de consumo (RN-009).</summary>
public class MiInstitucionViewModelTests
{
    private readonly IServicioInstitucion _servicio = Substitute.For<IServicioInstitucion>();

    private static MedidorPlan Medidor(decimal usados, int maximo, int porcentaje) =>
        new(usados, maximo, porcentaje, porcentaje >= 80, usados >= maximo);

    private static Institucion Institucion(
        int dias = 15,
        MedidorPlan? usuarios = null,
        MedidorPlan? equipos = null,
        MedidorPlan? gb = null,
        DateOnly? vigente = null,
        string plan = "Clínica") =>
        new(
            Guid.NewGuid(), "Clínica San Rafael", "900.123.456-7", dias, plan, vigente ?? new DateOnly(2027, 6, 30),
            new ConsumoPlan(usuarios ?? Medidor(3, 7, 42), equipos ?? Medidor(420, 1000, 42), gb ?? Medidor(1.25m, 25, 5), false));

    private async Task<MiInstitucionViewModel> AbrirAsync(Institucion? institucion = null)
    {
        _servicio.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(institucion ?? Institucion());
        var vm = new MiInstitucionViewModel(_servicio);
        await vm.CargarCommand.ExecuteAsync(null);
        return vm;
    }

    // ---------- CA-HU-024-03: plan, vigencia y consumo ----------

    [Fact]
    public async Task Cargar_MuestraLosDatosYElPlanConSuValorYVigencia()
    {
        var vm = await AbrirAsync();

        Assert.Equal(("Clínica San Rafael", "900.123.456-7", "15"), (vm.Nombre, vm.Nit, vm.Dias));
        Assert.Equal("Plan Clínica", vm.TituloPlan);
        Assert.Equal("$380.000 COP/mes", vm.ValorPlan);
        Assert.Equal("30/06/2027", vm.VigenteHasta);
        Assert.Equal(["Usuarios de gestión", "Equipos activos", "Documentos"], vm.Medidores.Select(m => m.Etiqueta));
        Assert.Equal(["3 de 7", "420 de 1.000", "1,25 de 25"], vm.Medidores.Select(m => m.Uso));
    }

    [Fact]
    public async Task SinVigenciaYPlanAMedida_LoDiceSinInventar()
    {
        var vm = await AbrirAsync(Institucion(plan: "Red de Salud") with { VigenteHasta = null });

        Assert.Equal("Sin fecha registrada", vm.VigenteHasta);
        Assert.Equal("Cotización", vm.ValorPlan);
    }

    [Theory]
    [InlineData(239, 79, TonoEstado.Exito, null)]
    [InlineData(240, 80, TonoEstado.Advertencia, "Usado el 80 %.")]
    [InlineData(300, 100, TonoEstado.Critico, "Límite alcanzado: no se pueden crear más (RN-009).")]
    public async Task CA_HU_024_03_TonoYNotaSegunElConsumo(int usados, int porcentaje, TonoEstado tono, string? nota)
    {
        var vm = await AbrirAsync(Institucion(equipos: Medidor(usados, 300, porcentaje)));

        var equipos = vm.Medidores[1];
        Assert.Equal((tono, nota), (equipos.Tono, equipos.Nota));
        Assert.Equal(porcentaje, equipos.Barra);
    }

    [Fact]
    public void Medidor_SinUso_DejaVerUnPocoDeBarraYUsaDecimalesEnGb()
    {
        var medidor = MedidorVista.De("Documentos", "GB", new MedidorPlan(0.04m, 5, 0, false, false), decimales: true);

        Assert.Equal(2, medidor.Barra);
        Assert.Equal("0,04 de 5", medidor.Uso);
    }

    [Fact]
    public async Task ErrorAlCargar_OfreceReintentar()
    {
        _servicio.ObtenerAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));
        var vm = new MiInstitucionViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(vm.HayDatos);
        Assert.Equal(("Sin conexión", "MSG-011", true), (vm.Aviso!.Titulo, vm.Aviso.Codigo, vm.Aviso.PuedeReintentar));
    }

    // ---------- CA-HU-024-01: guardar la anticipación de las alertas ----------

    [Fact]
    public async Task CA_HU_024_01_Guardar_EnviaLosDatosYConfirmaLosDias()
    {
        var vm = await AbrirAsync();
        _servicio.ActualizarAsync(Arg.Any<DatosInstitucion>(), Arg.Any<CancellationToken>()).Returns(Institucion(dias: 30));
        vm.Nombre = "  Clínica San Rafael ";
        vm.Dias = "30";

        await vm.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).ActualizarAsync(new DatosInstitucion("Clínica San Rafael", "900.123.456-7", 30), Arg.Any<CancellationToken>());
        Assert.Equal("Datos de la institución guardados", vm.Confirmacion!.Titulo);
        Assert.Contains("30 días de anticipación (RF-020)", vm.Confirmacion.Mensaje);
        Assert.Null(vm.Aviso);
    }

    [Theory]
    [InlineData("IPS", "900.123.456-7", "15", "nombre")]
    [InlineData("Clínica San Rafael", "900123456-7", "15", "nit")]
    [InlineData("Clínica San Rafael", "900.123.456-7", "6", "dias")]
    [InlineData("Clínica San Rafael", "900.123.456-7", "91", "dias")]
    [InlineData("Clínica San Rafael", "900.123.456-7", "quince", "dias")]
    public async Task DatoInvalido_MarcaElCampoConMsg008YNoEnvia(string nombre, string nit, string dias, string campo)
    {
        var vm = await AbrirAsync();
        (vm.Nombre, vm.Nit, vm.Dias) = (nombre, nit, dias);

        await vm.GuardarCommand.ExecuteAsync(null);

        var errores = new Dictionary<string, string?> { ["nombre"] = vm.ErrorNombre, ["nit"] = vm.ErrorNit, ["dias"] = vm.ErrorDias };
        Assert.Equal([campo], errores.Where(e => e.Value is not null).Select(e => e.Key));
        Assert.Equal("MSG-008", vm.Aviso!.Codigo);
        await _servicio.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default);
    }

    [Fact]
    public async Task EscribirEnUnCampo_QuitaSuError()
    {
        var vm = await AbrirAsync();
        vm.Nit = "900";
        await vm.GuardarCommand.ExecuteAsync(null);

        vm.Nit = "900.123.456-8";

        Assert.Null(vm.ErrorNit);
    }

    [Fact]
    public async Task NitDeOtraInstitucion_MuestraElErrorDeLaApi()
    {
        var vm = await AbrirAsync();
        _servicio.ActualizarAsync(Arg.Any<DatosInstitucion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi("TEN-001", "El NIT 800.111.222-3 ya está registrado en SITBIO.", new Dictionary<string, string>()));
        vm.Nit = "800.111.222-3";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(("No se pudieron guardar los datos", "TEN-001"), (vm.Aviso!.Titulo, vm.Aviso.Codigo));
        Assert.Null(vm.Confirmacion);
        Assert.False(vm.Guardando);
    }

    [Fact]
    public async Task ErrorPorCampoDeLaApi_SeMarcaEnElCampo()
    {
        var vm = await AbrirAsync();
        _servicio.ActualizarAsync(Arg.Any<DatosInstitucion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Datos no válidos", new Dictionary<string, string> { ["diasAnticipacionAlerta"] = "Usa un valor entre 7 y 90 días." }));

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Usa un valor entre 7 y 90 días.", vm.ErrorDias);
    }

    // ---------- Navegación: UI-019 solo abre Mi institución para AC-01 ----------

    [Theory]
    [InlineData("AC-01", true)]
    [InlineData("AC-02", false)]
    public void Fabrica_UI019_AbreMiInstitucionSoloParaElAdministrador(string rol, bool abre)
    {
        _servicio.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(Institucion());
        var servicios = new ServiceCollection()
            .AddSingleton(EquiposPrueba.Sesion(rol))
            .AddSingleton(_servicio)
            .AddTransient<MiInstitucionViewModel>()
            .BuildServiceProvider();

        var pantalla = new FabricaPantallas(servicios).Crear("UI-019");

        Assert.Equal(abre, pantalla is MiInstitucionViewModel);
    }
}
