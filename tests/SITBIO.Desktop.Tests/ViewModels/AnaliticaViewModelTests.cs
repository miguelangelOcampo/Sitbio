using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public sealed class AnaliticaViewModelTests
{
    private readonly IServicioAnalitica _servicio = Substitute.For<IServicioAnalitica>();

    private static Agrupamiento Resultado(int equipos, int k)
    {
        var lista = Enumerable.Range(0, equipos)
            .Select(i => new EquipoAgrupado(Guid.NewGuid(), $"BIO-{i}", i % k, new Caracteristicas(1, 0.5, 100 * (i + 1), 10 * (i + 1))))
            .ToList();
        var grupos = Enumerable.Range(0, k).Select(g => new GrupoAnalitica(g, 1, new Caracteristicas(1, 0.25, 500, 30))).ToList();
        return new Agrupamiento(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, equipos * 2, k, grupos, lista);
    }

    [Fact]
    public async Task Carga_puntos_escalados_y_resumen_por_grupo()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(4, 2));
        var vm = new AnaliticaViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.HayDatos);
        Assert.Equal(4, vm.Puntos.Count);
        Assert.All(vm.Puntos, p => Assert.InRange(p.X, 0, AnaliticaViewModel.Ancho));
        Assert.All(vm.Puntos, p => Assert.InRange(p.Y, 0, AnaliticaViewModel.Alto));
        Assert.Equal(0, vm.Puntos[^1].Y);
        Assert.Equal(2, vm.Resumen.Count);
        Assert.Equal("25 %", vm.Resumen[0].Correctivos);
        Assert.Null(vm.AvisoGrupos);
    }

    [Fact]
    public async Task Mas_grupos_que_equipos_pide_reducirlos()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(3, 2));
        var vm = new AnaliticaViewModel(_servicio);
        await vm.CargarCommand.ExecuteAsync(null);

        vm.GruposSolicitados = 5;

        Assert.Equal("CA-HU-009-02", vm.AvisoGrupos?.Codigo);
    }

    [Fact]
    public async Task Sin_resultados_muestra_ANA_001()
    {
        _servicio.AgrupamientosAsync(default).ThrowsAsyncForAnyArgs(new ErrorApi("ANA-001", "Aún no hay resultados", new Dictionary<string, string>()));
        var vm = new AnaliticaViewModel(_servicio);

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(vm.HayDatos);
        Assert.Equal("ANA-001", vm.Aviso?.Codigo);
        // G-05: sin resultados del lote es informativo (azul), no un error (rojo); se puede reintentar.
        Assert.False(vm.Aviso!.EsError);
        Assert.True(vm.Aviso.PuedeReintentar);
    }
}
