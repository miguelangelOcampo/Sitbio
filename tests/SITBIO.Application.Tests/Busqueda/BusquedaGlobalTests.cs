using NSubstitute;
using Sitbio.Application.Busqueda;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Busqueda;

/// <summary>Búsqueda global (D-04): grupos por rol, alcance de AC-04 y AC-03, y límites del texto.</summary>
public class BusquedaGlobalTests
{
    private static readonly Guid Usuario = Guid.NewGuid();
    private readonly IRepositorioBusqueda _repositorio = Substitute.For<IRepositorioBusqueda>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();

    public BusquedaGlobalTests()
    {
        _contexto.UsuarioId.Returns(Usuario);
        _repositorio.EquiposAsync(default!, default, default).ReturnsForAnyArgs([new EquipoEncontradoDto(Guid.NewGuid(), "MON-01", "SN-1", "Monitor", "UCI")]);
        _repositorio.TiquetesAsync(default!, default, default, default).ReturnsForAnyArgs([new TiqueteEncontradoDto("TQT-000001", "No enciende", "recibido", "MON-01")]);
        _repositorio.OrdenesAsync(default!, default, default, default).ReturnsForAnyArgs([new OrdenEncontradaDto(Guid.NewGuid(), "TQT-000001", "correctivo", "asignada", "MON-01")]);
    }

    private BusquedaGlobal Caso(string rol)
    {
        _contexto.Rol.Returns(rol);
        return new BusquedaGlobal(_repositorio, _contexto);
    }

    [Fact]
    public async Task Ingeniero_BuscaEnLosTresGrupos_ConMaximoCincoYSinAcotar()
    {
        var resultado = await Caso(CodigosRol.IngenieroBiomedico).BuscarAsync("  mon ", CancellationToken.None);

        Assert.Equal((1, 1, 1), (resultado.Equipos.Count, resultado.Tiquetes.Count, resultado.Ordenes.Count));
        await _repositorio.Received(1).EquiposAsync("mon", BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
        await _repositorio.Received(1).TiquetesAsync("mon", null, BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
        await _repositorio.Received(1).OrdenesAsync("mon", null, BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersonalClinico_SoloSusTiquetes_YSinOrdenes()
    {
        var resultado = await Caso(CodigosRol.PersonalClinico).BuscarAsync("TQT", CancellationToken.None);

        Assert.Single(resultado.Equipos);
        Assert.Empty(resultado.Ordenes);
        await _repositorio.Received(1).TiquetesAsync("TQT", Usuario, BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
        await _repositorio.DidNotReceiveWithAnyArgs().OrdenesAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Tecnico_SoloLasOrdenesAsignadasAEl()
    {
        await Caso(CodigosRol.TecnicoMantenimiento).BuscarAsync("TQT", CancellationToken.None);

        await _repositorio.Received(1).OrdenesAsync("TQT", Usuario, BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
        await _repositorio.Received(1).TiquetesAsync("TQT", null, BusquedaGlobal.MaximoPorGrupo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gerente_SoloEquipos()
    {
        var resultado = await Caso(CodigosRol.GerenteBiomedico).BuscarAsync("MON", CancellationToken.None);

        Assert.Single(resultado.Equipos);
        Assert.Empty(resultado.Tiquetes);
        Assert.Empty(resultado.Ordenes);
        await _repositorio.DidNotReceiveWithAnyArgs().TiquetesAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" M ")]
    public async Task MenosDeDosCaracteres_NoConsulta(string? q)
    {
        var resultado = await Caso(CodigosRol.IngenieroBiomedico).BuscarAsync(q, CancellationToken.None);

        Assert.Same(ResultadoBusquedaDto.Vacio, resultado);
        await _repositorio.DidNotReceiveWithAnyArgs().EquiposAsync(default!, default, default);
    }

    [Fact]
    public async Task TextoDemasiadoLargo_EsErrorDeValidacion()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            Caso(CodigosRol.IngenieroBiomedico).BuscarAsync(new string('x', BusquedaGlobal.LongitudMaxima + 1), CancellationToken.None));

        Assert.Contains("q", error.Errores.Keys);
    }
}
