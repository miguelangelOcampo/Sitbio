using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Infrastructure.Auditoria;

namespace Sitbio.Infrastructure.Tests.Auditoria;

public class RegistroAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);

    private readonly IBitacoraAuditoria _bitacora = Substitute.For<IBitacoraAuditoria>();
    private readonly IOrigenSolicitud _origen = Substitute.For<IOrigenSolicitud>();

    private sealed class TiempoFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private RegistroAuditoria Registro(bool conBitacora = true)
    {
        var servicios = new ServiceCollection();
        if (conBitacora)
        {
            servicios.AddSingleton(_bitacora);
        }

        return new RegistroAuditoria(servicios.BuildServiceProvider(), _origen, new TiempoFijo(), NullLogger<RegistroAuditoria>.Instance);
    }

    [Fact]
    public async Task Registra_ConLaHoraDelServidorYLaIpDeOrigen()
    {
        _origen.IpOrigen.Returns("10.0.0.5");
        var usuario = Guid.NewGuid();

        await Registro().RegistrarAsync(usuario, AccionesAuditoria.LoginOk, "usuario", usuario.ToString());

        await _bitacora.Received(1).RegistrarAsync(
            Arg.Is<EventoAuditoria>(e => e.UsuarioId == usuario && e.FechaHora == Ahora && e.IpOrigen == "10.0.0.5" && e.EntidadId == usuario.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiLaBitacoraFalla_NoInterrumpeLaOperacion()
    {
        _bitacora.RegistrarAsync(Arg.Any<EventoAuditoria>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("mongo"));

        await Registro().RegistrarAsync(Guid.NewGuid(), AccionesAuditoria.LoginOk, "usuario");
    }

    [Fact]
    public async Task SinBitacoraConfigurada_NoInterrumpeLaOperacion()
    {
        await Registro(conBitacora: false).RegistrarAsync(Guid.NewGuid(), AccionesAuditoria.AccesoDenegado, "usuarios");
    }

    [Fact]
    public async Task AccionInvalida_NoInterrumpeNiEscribe()
    {
        await Registro().RegistrarAsync(Guid.NewGuid(), "INVENTADA", "usuarios");

        await _bitacora.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }
}
