using NSubstitute;
using Sitbio.Application.Analitica;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Tests.Analitica;

/// <summary>I-04 (HU-008): recálculo de la analítica bajo demanda, un recálculo por institución a la vez.</summary>
public sealed class RecalculoAnaliticaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private readonly IRepositorioAnalitica _repositorio = Substitute.For<IRepositorioAnalitica>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly CoordinadorRecalculos _coordinador = new(new Reloj());

    private RecalculoAnalitica Caso(Guid tenant)
    {
        var contexto = Substitute.For<IContextoUsuario>();
        contexto.TenantId.Returns(tenant);
        contexto.UsuarioId.Returns(Guid.NewGuid());
        return new RecalculoAnalitica(_coordinador, _repositorio, contexto, _auditoria);
    }

    [Fact]
    public async Task Solicitar_EncolaYAudita_YNoAdmiteOtroEnCursoDeLaMismaInstitucion()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var estado = await Caso(a).SolicitarAsync(default);

        Assert.Equal((EstadosRecalculo.EnCurso, Ahora), (estado.Estado, estado.IniciadoEn));
        Assert.True(_coordinador.Pendientes.TryRead(out var encolado));
        Assert.Equal(a, encolado);
        await _auditoria.Received(1).RegistrarAsync(
            Arg.Any<Guid>(), AccionesAuditoria.Actualizar, RecalculoAnalitica.Entidad, a.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Caso(a).SolicitarAsync(default));
        Assert.Equal(RecalculoAnalitica.CodigoEnCurso, error.Codigo);

        // Otra institución no espera a la primera.
        Assert.Equal(EstadosRecalculo.EnCurso, (await Caso(b).SolicitarAsync(default)).Estado);
    }

    [Fact]
    public async Task Estado_InformaElResultadoYLaFechaDelUltimoCalculo()
    {
        var tenant = Guid.NewGuid();
        var generado = Ahora.AddDays(-1);
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs(new ResultadoAnalitica(generado, generado, 10, 3, [], []));

        var inicial = await Caso(tenant).EstadoAsync(default);
        Assert.Equal((EstadosRecalculo.SinSolicitud, (DateTimeOffset?)generado), (inicial.Estado, inicial.UltimoCalculo));

        await Caso(tenant).SolicitarAsync(default);
        _coordinador.Terminar(tenant);
        var terminado = await Caso(tenant).EstadoAsync(default);
        Assert.Equal((EstadosRecalculo.Terminado, (DateTimeOffset?)Ahora, (string?)null), (terminado.Estado, terminado.TerminadoEn, terminado.Mensaje));

        await Caso(tenant).SolicitarAsync(default);
        _coordinador.Fallar(tenant);
        var fallido = await Caso(tenant).EstadoAsync(default);
        Assert.Equal((EstadosRecalculo.Fallido, CoordinadorRecalculos.MensajeFallo), (fallido.Estado, fallido.Mensaje));
    }

    [Fact]
    public async Task SinResultadosDelLote_ElUltimoCalculoEsNulo()
    {
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs((ResultadoAnalitica?)null);

        Assert.Null((await Caso(Guid.NewGuid()).EstadoAsync(default)).UltimoCalculo);
    }

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }
}
