using NSubstitute;
using Sitbio.Application.Analitica;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Tests.Analitica;

public sealed class ConsultaAnaliticaTests
{
    private readonly IRepositorioAnalitica _repositorio = Substitute.For<IRepositorioAnalitica>();
    private readonly IPlacasEquipos _placas = Substitute.For<IPlacasEquipos>();

    [Fact]
    public async Task Sin_resultados_responde_ANA_001()
    {
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs((ResultadoAnalitica?)null);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => new ConsultaAnalitica(_repositorio, _placas).AgrupamientosAsync(default));

        Assert.Equal("ANA-001", error.Codigo);
    }

    [Fact]
    public async Task Agrega_las_placas_a_cada_equipo()
    {
        var conPlaca = Guid.NewGuid();
        var sinPlaca = Guid.NewGuid();
        var c = new CaracteristicasDto(2, 0.5, 1000, 60);
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs(new ResultadoAnalitica(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 4, 2, [new GrupoDto(0, 2, c)], [(conPlaca, 0, c), (sinPlaca, 1, c)]));
        _placas.PlacasAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string> { [conPlaca] = "BIO-1" });

        var dto = await new ConsultaAnalitica(_repositorio, _placas).AgrupamientosAsync(default);

        Assert.Equal(2, dto.K);
        Assert.Equal("BIO-1", dto.Equipos[0].PlacaInventario);
        Assert.Null(dto.Equipos[1].PlacaInventario);
        Assert.Equal(1, dto.Equipos[1].Grupo);
    }
}
