using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Usuarios;

public class GestionUsuariosTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly IRepositorioUsuarios _repositorio = Substitute.For<IRepositorioUsuarios>();
    private readonly IHasherContrasenas _hasher = Substitute.For<IHasherContrasenas>();
    private readonly IGeneradorClaves _claves = Substitute.For<IGeneradorClaves>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly GestionUsuarios _caso;

    public GestionUsuariosTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(Guid.NewGuid());
        _contexto.Rol.Returns(CodigosRol.AdministradorSistema);
        _claves.Generar().Returns("Temporal-2026#");
        _hasher.Hash("Temporal-2026#").Returns("hash-temporal");
        _repositorio.RolPorCodigoAsync("AC-03", Arg.Any<CancellationToken>()).Returns(new RolAsignable(3, "AC-03"));
        _repositorio.RolPorCodigoAsync("AC-01", Arg.Any<CancellationToken>()).Returns(new RolAsignable(1, "AC-01"));
        _caso = new GestionUsuarios(_repositorio, _hasher, _claves, _contexto);
    }

    private static DatosUsuario Datos(string correo = "nuevo@clinica.co", string rol = "AC-03") =>
        new("Laura", "Gómez", correo, null, rol);

    private Usuario Existente(string rol = "AC-03")
    {
        var usuario = new Usuario(Tenant, rol == "AC-01" ? 1 : 3, "Carlos", "Pérez", "carlos@clinica.co", "hash");
        _repositorio.ObtenerAsync(usuario.Id, Arg.Any<CancellationToken>()).Returns((usuario, rol));
        return usuario;
    }

    [Fact]
    public async Task Crear_EnElTenantDelTokenConClaveTemporalCifrada()
    {
        Usuario? agregado = null;
        _repositorio.Agregar(Arg.Do<Usuario>(u => agregado = u));

        var creado = await _caso.CrearAsync(Datos(" Nuevo@Clinica.CO "), CancellationToken.None);

        Assert.Equal("Temporal-2026#", creado.ClaveTemporal);
        Assert.NotNull(agregado);
        Assert.Equal(Tenant, agregado!.TenantId);
        Assert.Equal("hash-temporal", agregado.ContrasenaHash);
        Assert.True(agregado.CambioContrasenaObligatorio);
        Assert.Equal("nuevo@clinica.co", creado.Usuario.Correo);
        Assert.Equal("AC-03", creado.Usuario.Rol);
        Assert.Equal(UsuarioDto.Activo, creado.Usuario.Estado);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_CorreoEnUsoEnCualquierInstitucion_Responde409SinRevelarCual()
    {
        _repositorio.CorreoEnUsoAsync("nuevo@clinica.co", null, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CrearAsync(Datos(), CancellationToken.None));

        Assert.Equal(GestionUsuarios.CodigoCorreoEnUso, error.Codigo);
        Assert.Equal("Ya existe un usuario con este correo.", error.Message);
        _repositorio.DidNotReceiveWithAnyArgs().Agregar(default!);
    }

    [Theory]
    [InlineData("AC-99")]
    [InlineData("")]
    public async Task Crear_RolInvalido_Responde400(string rol)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(Datos(rol: rol), CancellationToken.None));

        Assert.Contains("rol", error.Errores.Keys);
    }

    [Fact]
    public async Task Crear_DatosInvalidos_Responde400PorCampo()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.CrearAsync(new DatosUsuario("", "Gómez", "nuevo@clinica.co", null, "AC-03"), CancellationToken.None));

        Assert.Contains("nombres", error.Errores.Keys);
    }

    [Fact]
    public async Task Obtener_DeOtroTenantOInexistente_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // El usuario de la sesión es el mismo que se intenta modificar.
    private Usuario AdministradorDeLaSesion()
    {
        var administrador = Existente(rol: "AC-01");
        _contexto.UsuarioId.Returns(administrador.Id);
        return administrador;
    }

    [Fact]
    public async Task Desactivar_LaPropiaCuenta_Responde409()
    {
        var administrador = AdministradorDeLaSesion();

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CambiarEstadoAsync(administrador.Id, "desactivar", CancellationToken.None));

        Assert.Equal(GestionUsuarios.CodigoPropiaCuenta, error.Codigo);
        Assert.True(administrador.EstadoActivo);
    }

    [Fact]
    public async Task Actualizar_QuitarseElRolDeAdministrador_Responde409()
    {
        var administrador = AdministradorDeLaSesion();

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.ActualizarAsync(administrador.Id, Datos("admin@clinica.co", "AC-03"), CancellationToken.None));

        Assert.Equal(GestionUsuarios.CodigoPropiaCuenta, error.Codigo);
        Assert.Equal(1, administrador.RolId);
    }

    [Theory]
    [InlineData("desactivar", UsuarioDto.Inactivo)]
    [InlineData("reactivar", UsuarioDto.Activo)]
    public async Task CambiarEstado_DesactivaYReactiva(string accion, string estado)
    {
        var usuario = Existente();

        var dto = await _caso.CambiarEstadoAsync(usuario.Id, accion, CancellationToken.None);

        Assert.Equal(estado, dto.Estado);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Desbloquear_CuentaBloqueadaPorRn006()
    {
        var usuario = Existente();
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarIntentoFallido();
        }

        var dto = await _caso.CambiarEstadoAsync(usuario.Id, "Desbloquear", CancellationToken.None);

        Assert.Equal(UsuarioDto.Activo, dto.Estado);
        Assert.Equal(0, usuario.IntentosFallidos);
    }

    [Fact]
    public async Task CambiarEstado_AccionDesconocida_Responde400()
    {
        var usuario = Existente();

        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CambiarEstadoAsync(usuario.Id, "borrar", CancellationToken.None));
    }

    [Fact]
    public async Task ClaveTemporal_SeDevuelveUnaVezYObligaACambiarla()
    {
        var usuario = Existente();

        var clave = await _caso.AsignarClaveTemporalAsync(usuario.Id, CancellationToken.None);

        Assert.Equal("Temporal-2026#", clave);
        Assert.Equal("hash-temporal", usuario.ContrasenaHash);
        Assert.True(usuario.CambioContrasenaObligatorio);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Listar_PaginacionFueraDeRango_Responde400(int pagina, int tamano)
    {
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.ListarAsync(pagina, tamano, CancellationToken.None));
    }

    [Fact]
    public async Task Listar_DevuelvePaginaConTotal()
    {
        var usuario = new Usuario(Tenant, 3, "Carlos", "Pérez", "carlos@clinica.co", "hash");
        _repositorio.ListarAsync(2, 10, Arg.Any<CancellationToken>()).Returns(([(usuario, "AC-03")], 11));

        Pagina<UsuarioDto> pagina = await _caso.ListarAsync(2, 10, CancellationToken.None);

        Assert.Equal(11, pagina.Total);
        Assert.Equal(2, pagina.Page);
        Assert.Equal("carlos@clinica.co", Assert.Single(pagina.Items).Correo);
    }
}
