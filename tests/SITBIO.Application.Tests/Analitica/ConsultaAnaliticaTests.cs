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
        _placas.PlacasAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, DatosEquipo> { [conPlaca] = new("BIO-1", "Monitor") });

        var dto = await new ConsultaAnalitica(_repositorio, _placas).AgrupamientosAsync(default);

        Assert.Equal(2, dto.K);
        Assert.Equal(("BIO-1", "Monitor"), (dto.Equipos[0].PlacaInventario, dto.Equipos[0].TipoEquipo));
        Assert.Null(dto.Equipos[1].PlacaInventario);
        Assert.Equal(1, dto.Equipos[1].Grupo);

        // Un resultado anterior a I-03 trae solo la agrupación por defecto.
        Assert.Equal(2, Assert.Single(dto.Agrupaciones).K);
    }

    [Fact]
    public async Task Entrega_las_agrupaciones_de_2_a_6_con_nombres_y_placas()
    {
        var equipo = Guid.NewGuid();
        var c = new CaracteristicasDto(2, 0.5, 1000, 60);
        AgrupacionGuardada Con(int k) => new(k, 10.0 / k, Enumerable.Range(0, k).Select(g => new GrupoDto(g, 1, c, $"Grupo {g}")).ToList(), [(equipo, k - 1, c)]);
        _repositorio.UltimoAsync(default).ReturnsForAnyArgs(new ResultadoAnalitica(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 4, 3, [new GrupoDto(0, 1, c, "Estables")], [(equipo, 0, c)], [Con(4), Con(2), Con(3)]));
        _placas.PlacasAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, DatosEquipo> { [equipo] = new("BIO-9", "Bomba") });

        var dto = await new ConsultaAnalitica(_repositorio, _placas).AgrupamientosAsync(default);

        Assert.Equal([2, 3, 4], dto.Agrupaciones.Select(a => a.K));
        var cuatro = dto.Agrupaciones[2];
        Assert.Equal((2.5, "Grupo 3", 3, "BIO-9"), (cuatro.Inercia, cuatro.Grupos[3].Nombre, cuatro.Equipos[0].Grupo, cuatro.Equipos[0].PlacaInventario));
    }
}
