using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-002 Cambio de contraseña (HU-013, RF-012).</summary>
public class CambioClaveViewModelTests
{
    private const string Temporal = "Tmp#7kPq2xWz9a";
    private const string Nueva = "Monitor#2026uci";

    private readonly IServicioSesion _servicio = Substitute.For<IServicioSesion>();

    private CambioClaveViewModel Crear(bool obligatorio = true) => new(_servicio, obligatorio);

    private static void Llenar(CambioClaveViewModel cambio, string actual = Temporal, string nueva = Nueva, string? confirmacion = null)
    {
        cambio.Actual = actual;
        cambio.Nueva = nueva;
        cambio.Confirmacion = confirmacion ?? nueva;
    }

    private static string EstadoDe(CambioClaveViewModel cambio, string regla) => cambio.Reglas.Single(r => r.Texto == regla).Estado;

    [Fact]
    public void Obligatorio_PideLaClaveTemporalYNoSePuedeCancelar()
    {
        var cambio = Crear();

        Assert.Equal("Crea tu nueva contraseña", cambio.Titulo);
        Assert.Equal("Contraseña temporal", cambio.EtiquetaActual);
        Assert.True(cambio.Obligatorio);
        Assert.False(cambio.EsVoluntario);
    }

    [Fact]
    public void Voluntario_PideLaContrasenaActual()
    {
        var cambio = Crear(obligatorio: false);

        Assert.Equal("Cambiar contraseña", cambio.Titulo);
        Assert.Equal("Contraseña actual", cambio.EtiquetaActual);
        Assert.True(cambio.EsVoluntario);
    }

    [Fact]
    public void LasReglasSeMarcanMientrasSeEscribe()
    {
        var cambio = Crear();

        cambio.Nueva = "monitor2026";

        Assert.Equal("Cumple", EstadoDe(cambio, ReglasClave.Longitud));
        Assert.Equal("Cumple", EstadoDe(cambio, ReglasClave.Numero));
        // Antes de intentar guardar, lo que falta no se pinta de rojo.
        Assert.Equal("Pendiente", EstadoDe(cambio, ReglasClave.MayusculaMinuscula));
        Assert.Equal("Pendiente", EstadoDe(cambio, ReglasClave.Simbolo));
        // La de datos personales la revisa la API.
        Assert.Equal("Pendiente", EstadoDe(cambio, ReglasClave.DatosPersonales));
    }

    [Fact]
    public async Task AlIntentarGuardar_MarcaEnRojoLasReglasQueFaltanConMsg008()
    {
        var cambio = Crear();
        Llenar(cambio, nueva: "monitor2026");

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Incumple", EstadoDe(cambio, ReglasClave.MayusculaMinuscula));
        Assert.Equal("Incumple", EstadoDe(cambio, ReglasClave.Simbolo));
        Assert.Equal("No cumple todas las reglas.", cambio.ErrorNueva);
        Assert.Equal("MSG-008", cambio.Aviso!.Codigo);
        await _servicio.DidNotReceiveWithAnyArgs().CambiarContrasenaAsync(default!, default!, default);
    }

    [Fact]
    public async Task CamposVaciosYConfirmacionDistinta_SeMarcan()
    {
        var cambio = Crear();
        Llenar(cambio, actual: string.Empty, confirmacion: "Otra#2026clave");

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Escribe la contraseña temporal que recibiste.", cambio.ErrorActual);
        Assert.Equal("Las contraseñas no coinciden.", cambio.ErrorConfirmacion);

        // Tras el primer intento, los errores se actualizan mientras se escribe.
        cambio.Confirmacion = Nueva;
        Assert.Null(cambio.ErrorConfirmacion);
    }

    [Fact]
    public async Task NuevaIgualALaActual_SeRechazaEnElCliente()
    {
        var cambio = Crear(obligatorio: false);
        Llenar(cambio, actual: Nueva);

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Debe ser diferente de la actual.", cambio.ErrorNueva);
    }

    [Fact]
    public async Task CA_HU_013_02_DatosValidos_EnviaYAvisaQueSeGuardo()
    {
        var cambio = Crear();
        var guardada = false;
        cambio.Guardada += (_, _) => guardada = true;
        Llenar(cambio);

        await cambio.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CambiarContrasenaAsync(Temporal, Nueva, Arg.Any<CancellationToken>());
        Assert.True(guardada);
        Assert.Equal(string.Empty, cambio.Nueva);
        Assert.Null(cambio.Aviso);
    }

    [Fact]
    public async Task LaApiRechazaLaClaveActual_SeMuestraEnEseCampo()
    {
        var cambio = Crear();
        Llenar(cambio);
        _servicio.CambiarContrasenaAsync(Temporal, Nueva, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "x", new Dictionary<string, string> { ["actual"] = "La contraseña actual no es correcta." }));

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("La contraseña actual no es correcta.", cambio.ErrorActual);
        Assert.Equal("MSG-008", cambio.Aviso!.Codigo);
    }

    [Fact]
    public async Task LaApiRechazaLaReglaDeDatosPersonales_SeMarcaEnRojo()
    {
        var cambio = Crear();
        Llenar(cambio);
        _servicio.CambiarContrasenaAsync(Temporal, Nueva, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "x", new Dictionary<string, string> { ["nueva"] = ReglasClave.DatosPersonales }));

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Incumple", EstadoDe(cambio, ReglasClave.DatosPersonales));
        Assert.Equal("No cumple todas las reglas.", cambio.ErrorNueva);

        // Al cambiar la clave vuelve a quedar pendiente hasta el siguiente intento.
        cambio.Nueva = "Bomba#Infusion2026";
        Assert.Equal("Pendiente", EstadoDe(cambio, ReglasClave.DatosPersonales));
    }

    [Fact]
    public async Task SinConexion_MuestraMsg011()
    {
        var cambio = Crear();
        Llenar(cambio);
        _servicio.CambiarContrasenaAsync(Temporal, Nueva, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));

        await cambio.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011"), (cambio.Aviso!.Titulo, cambio.Aviso.Codigo));
    }

    [Fact]
    public void Mostrar_AlternaLosCampos()
    {
        var cambio = Crear();

        cambio.AlternarMostrarCommand.Execute(null);
        Assert.True(cambio.Mostrar);
        cambio.AlternarMostrarCommand.Execute(null);
        Assert.False(cambio.Mostrar);
    }

    [Fact]
    public void Cancelar_AvisaAQuienAbrioLaPantalla()
    {
        var cambio = Crear();
        var cancelada = false;
        cambio.Cancelada += (_, _) => cancelada = true;

        cambio.CancelarCommand.Execute(null);

        Assert.True(cancelada);
    }
}
