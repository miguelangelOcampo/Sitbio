using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Seguridad;

public class IniciarSesionTests
{
    private const string Clave = "Clave-Correcta-2026";
    private const string Hash = "hash-de-la-clave";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 13, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioAcceso _repositorio = Substitute.For<IRepositorioAcceso>();
    private readonly IHasherContrasenas _hasher = Substitute.For<IHasherContrasenas>();
    private readonly IGeneradorTokens _generador = Substitute.For<IGeneradorTokens>();
    private readonly IEstablecerContextoUsuario _contexto = Substitute.For<IEstablecerContextoUsuario>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IniciarSesion _caso;

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    public IniciarSesionTests()
    {
        _hasher.Verificar(Clave, Hash).Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns("hash-ficticio");
        _generador.Generar(Arg.Any<Usuario>(), Arg.Any<string>())
            .Returns(c => new TokenEmitido("token.jwt", "jti-1", Ahora, Ahora.AddMinutes(30)));
        _caso = new IniciarSesion(_repositorio, _hasher, _generador, _contexto, _auditoria, new TiempoFijo());
    }

    private Usuario Registrar(bool institucionActiva = true)
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana.perez@demo-a.co", Hash);
        _repositorio.BuscarPorCorreoAsync("ana.perez@demo-a.co", Arg.Any<CancellationToken>())
            .Returns(new UsuarioParaAcceso(usuario, "AC-03", institucionActiva));
        return usuario;
    }

    private Task<ResultadoInicioSesion> Entrar(string clave, string correo = "ana.perez@demo-a.co") =>
        _caso.EjecutarAsync(new SolicitudInicioSesion(correo, clave), CancellationToken.None);

    [Fact]
    public async Task CredencialesValidas_EmiteTokenYRegistraLaSesion()
    {
        var usuario = Registrar();

        var resultado = await Entrar(Clave, " Ana.Perez@Demo-A.co ");

        Assert.Equal("token.jwt", resultado.Token);
        Assert.Equal("AC-03", resultado.Rol);
        Assert.Equal(Ahora.AddMinutes(30), resultado.ExpiraEn);
        Assert.Equal(Ahora, usuario.UltimoAcceso);
        _contexto.Received(1).Establecer(Tenant, usuario.Id, "AC-03");
        await _repositorio.Received(1).GuardarAsync(
            usuario, Arg.Is<SesionToken>(s => s.Jti == "jti-1" && s.UsuarioId == usuario.Id && s.TenantId == Tenant), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaveIncorrecta_RespondeMsg001YSumaUnIntento()
    {
        var usuario = Registrar();

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar("otra"));

        Assert.Equal("MSG-001", error.Codigo);
        Assert.Equal(IniciarSesion.MensajeCredenciales, error.Message);
        Assert.Equal(1, usuario.IntentosFallidos);
        await _repositorio.Received(1).GuardarAsync(usuario, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorreoInexistente_RespondeMsg001SinRevelarloYVerificaIgual()
    {
        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave, "nadie@demo-a.co"));

        Assert.Equal("MSG-001", error.Codigo);
        _hasher.Received(1).Verificar(Clave, "hash-ficticio");
        _contexto.DidNotReceiveWithAnyArgs().Establecer(default, default, default!);
    }

    [Fact]
    public async Task CincoFallos_BloqueanYElSextoConClaveCorrectaRespondeMsg002()
    {
        var usuario = Registrar();
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar("otra"));
        }

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave));

        Assert.True(usuario.EstadoBloqueo);
        Assert.Equal("MSG-002", error.Codigo);
        _generador.DidNotReceiveWithAnyArgs().Generar(default!, default!);
    }

    [Fact]
    public async Task InstitucionInactiva_RespondeMsg002()
    {
        Registrar(institucionActiva: false);

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave));

        Assert.Equal("MSG-002", error.Codigo);
    }

    [Theory]
    [InlineData("", Clave)]
    [InlineData("ana.perez@demo-a.co", "")]
    public async Task DatosVacios_RespondeErrorDeValidacion(string correo, string clave)
    {
        await Assert.ThrowsAsync<ErrorValidacion>(() => Entrar(clave, correo));
    }

    private Task Registrado(Usuario usuario, string accion, string? motivo = null) =>
        _auditoria.Received(1).RegistrarAsync(
            usuario.Id,
            accion,
            IniciarSesion.EntidadUsuario,
            usuario.Id.ToString(),
            motivo is null ? Arg.Any<IReadOnlyDictionary<string, string>?>() : Arg.Is<IReadOnlyDictionary<string, string>?>(d => d != null && d["motivo"] == motivo),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task HU010_CredencialesValidas_RegistranLoginOk()
    {
        var usuario = Registrar();

        await Entrar(Clave);

        await Registrado(usuario, AccionesAuditoria.LoginOk);
    }

    [Fact]
    public async Task HU010_ClaveIncorrecta_RegistraLoginFallidoPorCredenciales()
    {
        var usuario = Registrar();

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar("otra"));

        await Registrado(usuario, AccionesAuditoria.LoginFallido, "credenciales");
    }

    [Fact]
    public async Task HU010_CuentaBloqueada_RegistraLoginFallidoPorBloqueo()
    {
        var usuario = Registrar();
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarIntentoFallido();
        }

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave));

        await Registrado(usuario, AccionesAuditoria.LoginFallido, "bloqueada");
    }

    [Fact]
    public async Task HU010_InstitucionInactiva_RegistraLoginFallidoNoDisponible()
    {
        var usuario = Registrar(institucionActiva: false);

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave));

        await Registrado(usuario, AccionesAuditoria.LoginFallido, "no_disponible");
    }

    [Fact]
    public async Task HU010_CorreoInexistente_NoSeRegistra()
    {
        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar(Clave, "nadie@demo-a.co"));

        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task HU010_ElDetalleNoLlevaCorreoNiClave()
    {
        Registrar();

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Entrar("otra"));

        var detalle = (IReadOnlyDictionary<string, string>?)_auditoria.ReceivedCalls().Single().GetArguments()[4];
        Assert.DoesNotContain(detalle!.Values, v => v.Contains('@') || v.Contains("otra", StringComparison.Ordinal));
    }
}
