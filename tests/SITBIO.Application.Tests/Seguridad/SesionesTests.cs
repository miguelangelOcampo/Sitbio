using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Seguridad;

/// <summary>HU-012: cierre de sesión y renovación del token con revocación por jti.</summary>
public class SesionesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 13, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioSesiones _repositorio = Substitute.For<IRepositorioSesiones>();
    private readonly IGeneradorTokens _generador = Substitute.For<IGeneradorTokens>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly Usuario _usuario = new(Tenant, 3, "Ana", "Pérez", "ana.perez@demo-a.co", "hash");

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    public SesionesTests()
    {
        _generador.Generar(Arg.Any<Usuario>(), Arg.Any<string>()).Returns(new TokenEmitido("token.nuevo", "jti-nuevo", Ahora, Ahora.AddMinutes(30)));
    }

    private SesionToken Sesion(string jti = "jti-actual", int minutosDesdeEmision = 10, Guid? usuarioId = null)
    {
        var emision = Ahora.AddMinutes(-minutosDesdeEmision);
        var sesion = new SesionToken(Tenant, usuarioId ?? _usuario.Id, jti, emision, emision.AddMinutes(30));
        _repositorio.ObtenerAsync(jti, Arg.Any<CancellationToken>()).Returns(sesion);
        return sesion;
    }

    private void UsuarioDisponible(bool institucionActiva = true) =>
        _repositorio.BuscarUsuarioAsync(_usuario.Id, Arg.Any<CancellationToken>()).Returns(new UsuarioParaAcceso(_usuario, "AC-03", institucionActiva));

    private CerrarSesion Cerrar() => new(_repositorio, _auditoria, new TiempoFijo());

    private RenovarSesion Renovar() => new(_repositorio, _generador, new TiempoFijo());

    [Fact]
    public async Task CA_HU_012_02_CerrarSesion_RevocaElJtiDelToken()
    {
        var sesion = Sesion();

        await Cerrar().EjecutarAsync("jti-actual", null, CancellationToken.None);

        Assert.Equal(Ahora, sesion.FechaRevocacion);
        Assert.Equal(MotivosRevocacion.CierreDeSesion, sesion.MotivoRevocacion);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CA_HU_012_01_CerrarPorInactividad_GuardaEseMotivo()
    {
        var sesion = Sesion();

        await Cerrar().EjecutarAsync("jti-actual", " Inactividad ", CancellationToken.None);

        Assert.Equal(MotivosRevocacion.Inactividad, sesion.MotivoRevocacion);
    }

    [Fact]
    public async Task CerrarSesion_YaRevocada_NoCambiaNada()
    {
        var sesion = Sesion();
        sesion.Revocar(Ahora.AddMinutes(-1), MotivosRevocacion.Renovacion);

        await Cerrar().EjecutarAsync("jti-actual", "voluntario", CancellationToken.None);

        Assert.Equal(MotivosRevocacion.Renovacion, sesion.MotivoRevocacion);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CerrarSesion_MotivoLibre_Responde400SinGuardarElTexto()
    {
        Sesion();

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cerrar().EjecutarAsync("jti-actual", "porque sí", CancellationToken.None));

        Assert.True(error.Errores.ContainsKey("motivo"));
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("jti-desconocido")]
    public async Task CerrarSesion_SinSesionRegistrada_Responde401(string? jti)
    {
        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Cerrar().EjecutarAsync(jti, null, CancellationToken.None));
    }

    [Fact]
    public async Task Renovar_RevocaElJtiAnteriorYRegistraUnoNuevo()
    {
        var anterior = Sesion();
        UsuarioDisponible();

        var resultado = await Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None);

        Assert.Equal("token.nuevo", resultado.Token);
        Assert.Equal(Ahora.AddMinutes(30), resultado.ExpiraEn);
        Assert.Equal("AC-03", resultado.Rol);
        Assert.Equal(MotivosRevocacion.Renovacion, anterior.MotivoRevocacion);
        _repositorio.Received(1).Agregar(Arg.Is<SesionToken>(s => s.Jti == "jti-nuevo" && s.UsuarioId == _usuario.Id && s.TenantId == Tenant));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renovar_SesionRevocada_Responde401SinEmitirToken()
    {
        Sesion().Revocar(Ahora.AddMinutes(-1), MotivosRevocacion.CierreDeSesion);
        UsuarioDisponible();

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None));

        _generador.DidNotReceiveWithAnyArgs().Generar(default!, default!);
    }

    [Fact]
    public async Task Renovar_SesionExpirada_Responde401()
    {
        Sesion(minutosDesdeEmision: 31);
        UsuarioDisponible();

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None));
    }

    [Fact]
    public async Task Renovar_SesionDeOtroUsuario_Responde401()
    {
        Sesion(usuarioId: Guid.NewGuid());
        UsuarioDisponible();

        await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None));
    }

    [Fact]
    public async Task Renovar_CuentaBloqueada_CierraLaSesionConMsg002()
    {
        var sesion = Sesion();
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            _usuario.RegistrarIntentoFallido();
        }

        UsuarioDisponible();

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None));

        Assert.Equal("MSG-002", error.Codigo);
        Assert.Equal(MotivosRevocacion.AccesoNoDisponible, sesion.MotivoRevocacion);
        _generador.DidNotReceiveWithAnyArgs().Generar(default!, default!);
    }

    [Fact]
    public async Task Renovar_InstitucionInactiva_CierraLaSesionConMsg002()
    {
        var sesion = Sesion();
        UsuarioDisponible(institucionActiva: false);

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Renovar().EjecutarAsync(_usuario.Id, "jti-actual", CancellationToken.None));

        Assert.Equal("MSG-002", error.Codigo);
        Assert.True(sesion.EstaRevocada);
    }

    [Theory]
    [InlineData(null, "voluntario")]
    [InlineData("inactividad", "inactividad")]
    public async Task HU010_CerrarSesion_RegistraCierreSesionConElMotivo(string? motivo, string esperado)
    {
        var sesion = Sesion();

        await Cerrar().EjecutarAsync("jti-actual", motivo, CancellationToken.None);

        await _auditoria.Received(1).RegistrarAsync(
            _usuario.Id,
            AccionesAuditoria.CierreSesion,
            "sesion",
            sesion.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["motivo"] == esperado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU010_CerrarSesionYaRevocada_NoRegistraOtraVez()
    {
        Sesion().Revocar(Ahora.AddMinutes(-1), MotivosRevocacion.Renovacion);

        await Cerrar().EjecutarAsync("jti-actual", null, CancellationToken.None);

        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }
}
