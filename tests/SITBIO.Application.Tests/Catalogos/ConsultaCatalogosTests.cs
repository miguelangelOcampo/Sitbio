using NSubstitute;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Tests.Catalogos;

public class ConsultaCatalogosTests
{
    private readonly IRepositorioCatalogos _repositorio = Substitute.For<IRepositorioCatalogos>();
    private readonly ConsultaCatalogos _caso;

    public ConsultaCatalogosTests()
    {
        _repositorio.TiposEquipoAsync(Arg.Any<CancellationToken>()).Returns([new CatalogoDto(Guid.NewGuid(), "Tensiómetro", true, "I", true, 12)]);
        _repositorio.UbicacionesAsync(Arg.Any<CancellationToken>()).Returns([new CatalogoDto(Guid.NewGuid(), "Urgencias", true)]);
        _repositorio.ProveedoresAsync(Arg.Any<CancellationToken>()).Returns([new CatalogoDto(Guid.NewGuid(), "Biomédica SAS", false)]);
        _repositorio.TiposFallaAsync(Arg.Any<CancellationToken>()).Returns([new CatalogoDto(Guid.NewGuid(), "No enciende", true)]);
        _caso = new ConsultaCatalogos(_repositorio);
    }

    [Theory]
    [InlineData("tipos-equipo", "Tensiómetro")]
    [InlineData("ubicaciones", "Urgencias")]
    [InlineData("Proveedores", "Biomédica SAS")]
    [InlineData(" tipos-falla ", "No enciende")]
    public async Task CadaTipo_LeeSuCatalogo(string tipo, string esperado)
    {
        var elementos = await _caso.ListarAsync(tipo, CancellationToken.None);

        Assert.Equal(esperado, Assert.Single(elementos).Nombre);
    }

    [Fact]
    public async Task TipoDeEquipo_TraeLosValoresRegulatoriosPorDefecto()
    {
        var tipo = Assert.Single(await _caso.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None));

        Assert.Equal("I", tipo.ClaseRiesgo);
        Assert.True(tipo.RequiereCalibracion);
        Assert.Equal(12, tipo.PeriodicidadCalibracionMeses);
    }

    [Theory]
    [InlineData("Tipos-Equipo", "tipos-equipo")]
    [InlineData(" tipos-falla", "tipos-falla")]
    [InlineData("campos-dinamicos", null)]
    [InlineData(null, null)]
    public void Normalizar_SoloLosCuatroCatalogosDeHU025(string? tipo, string? esperado)
    {
        Assert.Equal(esperado, TiposCatalogo.Normalizar(tipo));
    }

    [Fact]
    public async Task CatalogoDesconocido_NoExiste()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ListarAsync("campos-dinamicos", CancellationToken.None));
    }
}
