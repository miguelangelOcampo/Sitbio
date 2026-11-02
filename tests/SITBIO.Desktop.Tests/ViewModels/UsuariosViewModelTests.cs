using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-004 (HU-011): la pantalla muestra lo que responde la API; las reglas las aplica el servidor.</summary>
public class UsuariosViewModelTests
{
    private static readonly Guid YoId = Guid.NewGuid();

    private readonly IServicioUsuarios _servicio = Substitute.For<IServicioUsuarios>();
    private readonly IPortapapeles _portapapeles = Substitute.For<IPortapapeles>();

    private static UsuarioResumen Usuario(string nombres, string rol, string estado = "Activo", Guid? id = null, string? correo = null) =>
        new(id ?? Guid.NewGuid(), nombres, "Prueba", correo ?? $"{nombres.ToLowerInvariant()}@demo.co", null, rol, estado, false, null);

    private static SesionActual SesionDe(Guid usuarioId)
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario($"{B64("{}")}.{B64($$"""{"sub":"{{usuarioId}}"}""")}.firma", DateTimeOffset.Now.AddMinutes(30), "AC-01", false));
        return sesion;
    }

    private async Task<UsuariosViewModel> PantallaCon(params UsuarioResumen[] usuarios)
    {
        _servicio.ListarAsync(Arg.Any<CancellationToken>()).Returns(usuarios);
        var pantalla = new UsuariosViewModel(_servicio, SesionDe(YoId), _portapapeles);
        await pantalla.CargarCommand.ExecuteAsync(null);
        return pantalla;
    }

    private static ErrorApi Error(string? codigo, string mensaje = "detalle", Dictionary<string, string>? errores = null, bool sinConexion = false) =>
        new(codigo, mensaje, errores ?? [], sinConexion);

    [Fact]
    public async Task Cargar_CalculaLosIndicadoresYMarcaLaPropiaCuenta()
    {
        var pantalla = await PantallaCon(
            Usuario("Sofía", "AC-01", id: YoId),
            Usuario("Laura", "AC-03", "Bloqueado"),
            Usuario("Mateo", "AC-04", "Inactivo"),
            Usuario("Ana", "AC-02"));

        Assert.Equal(4, pantalla.Total);
        Assert.Equal(2, pantalla.Activos);
        Assert.Equal(1, pantalla.Bloqueados);
        Assert.Equal(1, pantalla.Inactivos);
        Assert.True(pantalla.HayBloqueados);
        Assert.Equal("Usuarios (4)", pantalla.TituloPestanaUsuarios);
        Assert.Equal("Sofía Prueba (tú)", pantalla.Todos.Single(u => u.Id == YoId).NombreMostrado);
        Assert.Equal("Nunca", pantalla.Todos[0].UltimoAcceso);
    }

    [Fact]
    public async Task Filtros_PorTextoSinTildesRolYEstado()
    {
        var pantalla = await PantallaCon(Usuario("Sofía", "AC-01"), Usuario("Laura", "AC-03", "Bloqueado"), Usuario("Mateo", "AC-03"));

        pantalla.Busqueda = "sofia";
        Assert.Equal(["Sofía Prueba"], pantalla.Filtrados.Select(u => u.NombreCompleto));

        pantalla.Busqueda = string.Empty;
        pantalla.FiltroRol = UsuariosViewModel.OpcionesRol.Single(o => o.Valor == "AC-03");
        pantalla.FiltroEstado = UsuariosViewModel.OpcionesEstado.Single(o => o.Valor == "Bloqueado");

        Assert.Equal(["Laura Prueba"], pantalla.Filtrados.Select(u => u.NombreCompleto));
        Assert.Equal("1 de 3 usuarios", pantalla.ResumenLista);
    }

    [Fact]
    public async Task PropiaCuenta_NoOfreceDesactivar()
    {
        var pantalla = await PantallaCon(Usuario("Sofía", "AC-01", id: YoId), Usuario("Ana", "AC-02"), Usuario("Mateo", "AC-04", "Inactivo"));

        var propia = pantalla.Todos.Single(u => u.EsPropio);
        Assert.False(propia.PuedeDesactivar);
        Assert.False(propia.PuedeReactivar);
        Assert.True(pantalla.Todos.Single(u => u.NombreCompleto == "Ana Prueba").PuedeDesactivar);
        var inactivo = pantalla.Todos.Single(u => u.Estado == "Inactivo");
        Assert.True(inactivo.PuedeReactivar);
        Assert.False(inactivo.PuedeRestablecer);
    }

    [Fact]
    public async Task Cargar_SinConexion_MuestraMsg011ConReintentar()
    {
        _servicio.ListarAsync(Arg.Any<CancellationToken>()).ThrowsAsync(Error(null, Mensajes.Msg011, sinConexion: true));
        var pantalla = new UsuariosViewModel(_servicio, SesionDe(YoId), _portapapeles);

        await pantalla.CargarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-011", pantalla.Aviso?.Codigo);
        Assert.True(pantalla.Aviso?.PuedeReintentar);
        Assert.False(pantalla.Cargando);
    }

    [Fact]
    public async Task CA_HU_011_01_Crear_ValidaAntesDeLlamarALaApi()
    {
        var pantalla = await PantallaCon();
        pantalla.NuevoUsuarioCommand.Execute(null);
        pantalla.Formulario!.Correo = "no-es-correo";

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-008", pantalla.Formulario.Aviso?.Codigo);
        Assert.NotNull(pantalla.Formulario.ErrorNombres);
        Assert.NotNull(pantalla.Formulario.ErrorCorreo);
        await _servicio.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task CA_HU_011_01_Crear_MuestraLaClaveTemporalUnaVezYAgregaLaFila()
    {
        var pantalla = await PantallaCon(Usuario("Sofía", "AC-01", id: YoId));
        var nuevo = Usuario("Laura", "AC-03", correo: "laura@demo.co");
        _servicio.CrearAsync(Arg.Any<DatosUsuario>(), Arg.Any<CancellationToken>()).Returns(new UsuarioCreado(nuevo, "Tmp-Clave-123"));
        pantalla.NuevoUsuarioCommand.Execute(null);
        var formulario = pantalla.Formulario!;
        formulario.Nombres = " Laura ";
        formulario.Apellidos = "Prueba";
        formulario.Correo = "laura@demo.co";
        formulario.Rol = GuiaRoles.Buscar("AC-03");

        await pantalla.GuardarCommand.ExecuteAsync(null);

        await _servicio.Received(1).CrearAsync(new DatosUsuario("Laura", "Prueba", "laura@demo.co", null, "AC-03"), Arg.Any<CancellationToken>());
        Assert.Null(pantalla.Formulario);
        Assert.Equal("Tmp-Clave-123", pantalla.ClaveEmitida?.Clave);
        Assert.Equal(2, pantalla.Total);

        pantalla.CopiarClaveCommand.Execute(null);
        _portapapeles.Received(1).Copiar("Tmp-Clave-123");

        pantalla.CerrarClaveCommand.Execute(null);
        Assert.Null(pantalla.ClaveEmitida);
    }

    [Fact]
    public async Task CA_HU_011_03_Crear_ConElCupoLleno_MuestraMsg019EnElFormulario()
    {
        var pantalla = await PantallaCon();
        _servicio.CrearAsync(Arg.Any<DatosUsuario>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error("RN-009", "Tu institución alcanzó el máximo de usuarios de gestión del plan Consultorio."));
        pantalla.CrearConRolGuiaCommand.Execute(null);
        var formulario = pantalla.Formulario!;
        formulario.Nombres = "Ana";
        formulario.Apellidos = "Ríos";
        formulario.Correo = "ana@demo.co";

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.Same(formulario, pantalla.Formulario);
        Assert.Equal("MSG-019", formulario.Aviso?.Codigo);
        Assert.Contains("plan Consultorio", formulario.Aviso?.Mensaje);
        Assert.Equal(0, pantalla.Total);
    }

    [Fact]
    public async Task Crear_CorreoEnUso_MarcaElCampoCorreo()
    {
        var pantalla = await PantallaCon();
        _servicio.CrearAsync(Arg.Any<DatosUsuario>(), Arg.Any<CancellationToken>()).ThrowsAsync(Error("USR-001", "Ya existe un usuario con este correo."));
        pantalla.NuevoUsuarioCommand.Execute(null);
        var formulario = pantalla.Formulario!;
        (formulario.Nombres, formulario.Apellidos, formulario.Correo) = ("Ana", "Ríos", "ana@demo.co");

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un usuario con este correo.", formulario.ErrorCorreo);
        Assert.Equal("MSG-008", formulario.Aviso?.Codigo);
    }

    [Fact]
    public async Task Crear_ErroresDeValidacionDeLaApi_SeMuestranEnSuCampo()
    {
        var pantalla = await PantallaCon();
        _servicio.CrearAsync(Arg.Any<DatosUsuario>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Error(null, "Uno o más datos no son válidos.", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["telefono"] = "Teléfono demasiado largo." }));
        pantalla.NuevoUsuarioCommand.Execute(null);
        var formulario = pantalla.Formulario!;
        (formulario.Nombres, formulario.Apellidos, formulario.Correo, formulario.Telefono) = ("Ana", "Ríos", "ana@demo.co", "1234");

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Teléfono demasiado largo.", formulario.ErrorTelefono);
    }

    [Fact]
    public async Task Editar_CambioDeRol_AvisaQueAplicaEnElProximoIngreso()
    {
        var ana = Usuario("Ana", "AC-04");
        var pantalla = await PantallaCon(ana);
        _servicio.ActualizarAsync(ana.Id, Arg.Any<DatosUsuario>(), Arg.Any<CancellationToken>()).Returns(ana with { Rol = "AC-02" });
        pantalla.EditarCommand.Execute(pantalla.Todos[0]);
        Assert.Equal("AC-04", pantalla.Formulario!.Rol?.Codigo);
        pantalla.Formulario.Rol = GuiaRoles.Buscar("AC-02");

        await pantalla.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("AC-02", pantalla.Todos[0].Rol);
        Assert.False(pantalla.Aviso?.EsError);
        Assert.Contains("próximo inicio de sesión", pantalla.Aviso?.Mensaje);
    }

    [Fact]
    public async Task Desactivar_PideConfirmacionAntesDeLlamarALaApi()
    {
        var ana = Usuario("Ana", "AC-02");
        var pantalla = await PantallaCon(ana);
        _servicio.CambiarEstadoAsync(ana.Id, "desactivar", Arg.Any<CancellationToken>()).Returns(ana with { Estado = "Inactivo" });

        pantalla.PedirDesactivarCommand.Execute(pantalla.Todos[0]);
        await _servicio.DidNotReceiveWithAnyArgs().CambiarEstadoAsync(default, default!, default);
        Assert.NotNull(pantalla.PorDesactivar);

        await pantalla.ConfirmarDesactivarCommand.ExecuteAsync(null);

        Assert.Null(pantalla.PorDesactivar);
        Assert.Equal("Inactivo", pantalla.Todos[0].Estado);
        Assert.Equal(1, pantalla.Inactivos);
    }

    [Fact]
    public async Task CA_HU_011_03_Reactivar_ConElCupoLleno_MuestraMsg019()
    {
        var ana = Usuario("Ana", "AC-02", "Inactivo");
        var pantalla = await PantallaCon(ana);
        _servicio.CambiarEstadoAsync(ana.Id, "reactivar", Arg.Any<CancellationToken>()).ThrowsAsync(Error("RN-009", "Límite del plan."));

        await pantalla.ReactivarCommand.ExecuteAsync(pantalla.Todos[0]);

        Assert.Equal("MSG-019", pantalla.Aviso?.Codigo);
        Assert.True(pantalla.Aviso?.EsError);
        Assert.Equal("Inactivo", pantalla.Todos[0].Estado);
        Assert.False(pantalla.Ocupado);
    }

    [Fact]
    public async Task Desbloquear_ActualizaLaFilaYConfirma()
    {
        var laura = Usuario("Laura", "AC-03", "Bloqueado");
        var pantalla = await PantallaCon(laura);
        _servicio.CambiarEstadoAsync(laura.Id, "desbloquear", Arg.Any<CancellationToken>()).Returns(laura with { Estado = "Activo" });

        await pantalla.DesbloquearCommand.ExecuteAsync(pantalla.Todos[0]);

        Assert.Equal(0, pantalla.Bloqueados);
        Assert.False(pantalla.HayBloqueados);
        Assert.False(pantalla.Aviso?.EsError);
    }

    [Fact]
    public async Task RestablecerClave_MuestraLaNuevaClave()
    {
        var laura = Usuario("Laura", "AC-03");
        var pantalla = await PantallaCon(laura);
        _servicio.AsignarClaveTemporalAsync(laura.Id, Arg.Any<CancellationToken>()).Returns("Otra-Clave-456");

        await pantalla.RestablecerClaveCommand.ExecuteAsync(pantalla.Todos[0]);

        Assert.Equal("Otra-Clave-456", pantalla.ClaveEmitida?.Clave);
        Assert.False(pantalla.ClaveEmitida?.EsNueva);
    }

    [Fact]
    public async Task GuiaDeRoles_CuentaActivosYVerUsuariosFiltraLaLista()
    {
        var pantalla = await PantallaCon(Usuario("Laura", "AC-03"), Usuario("Mateo", "AC-03"), Usuario("Diana", "AC-03", "Inactivo"), Usuario("Ana", "AC-02"));
        pantalla.MostrarRolesCommand.Execute(null);

        pantalla.ElegirRolGuiaCommand.Execute(GuiaRoles.Buscar("AC-03"));

        Assert.Equal(2, pantalla.RolesGuia.Single(r => r.Info.Codigo == "AC-03").Activos);
        Assert.True(pantalla.RolesGuia.Single(r => r.Info.Codigo == "AC-03").Seleccionado);
        Assert.Equal("Ver usuarios (3)", pantalla.TextoVerUsuarios);
        Assert.All(pantalla.Matriz, fila => Assert.True(fila.Celdas[2].Seleccionada));

        pantalla.VerUsuariosDelRolCommand.Execute(null);

        Assert.True(pantalla.VerUsuarios);
        Assert.Equal(3, pantalla.Filtrados.Count);
        Assert.All(pantalla.Filtrados, u => Assert.Equal("AC-03", u.Rol));
    }
}
