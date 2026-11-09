using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Seguridad;

/// <summary>HU-013 (RF-012, API-AUTH-PASSWORD): cambio de la propia contraseña.</summary>
public class CambiarContrasenaTests
{
    private const string Temporal = "Tmp#7kPq2xWz9a";
    private const string Nueva = "Monitor#2026uci";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioSesiones _repositorio = Substitute.For<IRepositorioSesiones>();
    private readonly IHasherContrasenas _hasher = Substitute.For<IHasherContrasenas>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly Usuario _usuario = new(Tenant, 3, "Ana", "Pérez", "ana.perez@demo-a.co", "hash-temporal");
    private readonly CambiarContrasena _caso;

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    public CambiarContrasenaTests()
    {
        _usuario.AsignarClaveTemporal("hash-temporal");
        _hasher.Verificar(Temporal, "hash-temporal").Returns(true);
        _hasher.Hash(Nueva).Returns("hash-nuevo");
        Disponible();
        _caso = new CambiarContrasena(_repositorio, _hasher, _auditoria, new TiempoFijo());
    }

    private void Disponible(bool institucionActiva = true) =>
        _repositorio.BuscarUsuarioAsync(_usuario.Id, Arg.Any<CancellationToken>()).Returns(new UsuarioParaAcceso(_usuario, "AC-03", institucionActiva));

    private SesionToken[] Abiertas()
    {
        var sesiones = new[]
        {
            new SesionToken(Tenant, _usuario.Id, "jti-actual", Ahora.AddMinutes(-2), Ahora.AddMinutes(28)),
            new SesionToken(Tenant, _usuario.Id, "jti-otro-equipo", Ahora.AddMinutes(-20), Ahora.AddMinutes(10)),
        };
        _repositorio.VigentesDelUsuarioAsync(_usuario.Id, Ahora, Arg.Any<CancellationToken>()).Returns(sesiones);
        return sesiones;
    }

    private Task Cambiar(string? actual = Temporal, string? nueva = Nueva) =>
        _caso.EjecutarAsync(_usuario.Id, "jti-actual", new SolicitudCambioContrasena(actual, nueva), CancellationToken.None);

    [Fact]
    public async Task CA_HU_013_02_ConLaClaveTemporal_GuardaElHashYDejaDeExigirElCambio()
    {
        await Cambiar();

        Assert.Equal("hash-nuevo", _usuario.ContrasenaHash);
        Assert.False(_usuario.CambioContrasenaObligatorio);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevocaLasDemasSesionesYConservaLaActual()
    {
        var sesiones = Abiertas();

        await Cambiar();

        Assert.False(sesiones[0].EstaRevocada);
        Assert.Equal(MotivosRevocacion.CambioContrasena, sesiones[1].MotivoRevocacion);
        Assert.Equal(Ahora, sesiones[1].FechaRevocacion);
    }

    [Fact]
    public async Task QuedaEnLaBitacoraSinLaClaveNiElCorreo()
    {
        IReadOnlyDictionary<string, string>? detalle = null;
        await _auditoria.RegistrarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Do<IReadOnlyDictionary<string, string>?>(d => detalle = d), Arg.Any<CancellationToken>());

        await Cambiar();

        await _auditoria.Received(1).RegistrarAsync(_usuario.Id, AccionesAuditoria.Actualizar, "usuario", _usuario.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        Assert.Equal(CambiarContrasena.DetalleObligatorio, detalle!["cambio"]);
        Assert.DoesNotContain(detalle.Values, v => v.Contains(Nueva) || v.Contains(Temporal) || v.Contains("ana.perez"));
    }

    [Fact]
    public async Task ClaveActualIncorrecta_Responde400EnEseCampoYNoCambiaNada()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar(actual: "otra-clave"));

        Assert.Equal([CambiarContrasena.MensajeActualIncorrecta], error.Errores["actual"]);
        Assert.Equal("hash-temporal", _usuario.ContrasenaHash);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NuevaQueNoCumpleLaPolitica_Responde400ConCadaRegla()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar(nueva: "perez2026"));

        Assert.Equal(
            [PoliticaContrasena.ReglaLongitud, PoliticaContrasena.ReglaMayusculaMinuscula, PoliticaContrasena.ReglaSimbolo, PoliticaContrasena.ReglaDatosPersonales],
            error.Errores["nueva"]);
    }

    [Fact]
    public async Task NuevaIgualALaActual_SeRechaza()
    {
        _hasher.Verificar(Nueva, "hash-temporal").Returns(true);

        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar(actual: Nueva, nueva: Nueva));

        Assert.Equal([CambiarContrasena.MensajeIgualALaActual], error.Errores["nueva"]);
    }

    [Fact]
    public async Task CamposVacios_PidenLaClaveTemporalYLaNueva()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Cambiar(actual: null, nueva: ""));

        Assert.Equal(["Escribe la contraseña temporal que recibiste."], error.Errores["actual"]);
        Assert.Equal(["Escribe la nueva contraseña."], error.Errores["nueva"]);
    }

    [Fact]
    public async Task CambioVoluntario_SeAnotaComoVoluntario()
    {
        _hasher.Hash("Primera#Clave2026").Returns("hash-propio");
        await Cambiar();
        _hasher.Verificar(Nueva, "hash-nuevo").Returns(true);

        await Cambiar(actual: Nueva, nueva: "Primera#Clave2026");

        await _auditoria.Received(1).RegistrarAsync(
            _usuario.Id,
            AccionesAuditoria.Actualizar,
            "usuario",
            Arg.Any<string?>(),
            Arg.Is<IReadOnlyDictionary<string, string>?>(d => d!["cambio"] == CambiarContrasena.DetalleVoluntario),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CuentaOInstitucionNoDisponibles_Responde401ConMsg002(bool bloqueada, bool institucionActiva)
    {
        if (bloqueada)
        {
            for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
            {
                _usuario.RegistrarIntentoFallido();
            }
        }

        Disponible(institucionActiva);

        var error = await Assert.ThrowsAsync<ErrorNoAutenticado>(() => Cambiar());

        Assert.Equal("MSG-002", error.Codigo);
    }
}
