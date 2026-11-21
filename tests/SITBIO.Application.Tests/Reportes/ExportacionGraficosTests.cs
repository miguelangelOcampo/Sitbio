using NSubstitute;
using Sitbio.Application.Analitica;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Indicadores;
using Sitbio.Application.Instituciones;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Reportes;

namespace Sitbio.Application.Tests.Reportes;

/// <summary>
/// J-03: Reportes e indicadores y Analítica en PDF con sus gráficos. Las imágenes se validan (PNG, tamaño y
/// cantidad); los valores de las tablas salen de los repositorios de la institución del token, no del cliente; cada
/// exportación queda en la bitácora.
/// </summary>
public class ExportacionGraficosTests
{
    /// <summary>PNG válido de 1 × 1 píxel.</summary>
    public const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private readonly IRepositorioIndicadores _indicadores = Substitute.For<IRepositorioIndicadores>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly IRepositorioAnalitica _analitica = Substitute.For<IRepositorioAnalitica>();
    private readonly IPlacasEquipos _placas = Substitute.For<IPlacasEquipos>();
    private readonly IRepositorioInstitucion _instituciones = Substitute.For<IRepositorioInstitucion>();
    private readonly IGeneradorReportes _generador = Substitute.For<IGeneradorReportes>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();

    public ExportacionGraficosTests()
    {
        _contexto.UsuarioId.Returns(_usuario);
        _contexto.TenantId.Returns(_tenant);
        _indicadores.DatosAsync(default, default, default).ReturnsForAnyArgs(new DatosIndicadores(4, 3, [TimeSpan.FromHours(5)], 2, 10, 1));
        _indicadores.ParqueAsync(default).ReturnsForAnyArgs(new ParqueDto(8, 1, 1, [new EquipoFueraDto(Guid.NewGuid(), "PL-1", "Monitor", "UCI")]));
        _generador.IndicadoresPdf(default!).ReturnsForAnyArgs([1]);
        _generador.AnaliticaPdf(default!).ReturnsForAnyArgs([2]);
    }

    private ExportacionGraficos Caso() => new(
        new ConsultaIndicadores(_indicadores, _intervenciones, TimeProvider.System),
        new ConsultaAnalitica(_analitica, _placas),
        _instituciones,
        _generador,
        _auditoria,
        _contexto,
        TimeProvider.System);

    private static GraficoAdjunto Grafico(string? titulo = "Mantenimientos por mes", string? png = Png1x1) => new(titulo, png);

    [Fact]
    public void Graficos_ValidaCadaImagen_YRecortaElTitulo()
    {
        var errores = new Dictionary<string, string[]>();
        var gif = Convert.ToBase64String("GIF89a-no-es-png"u8.ToArray());
        var grande = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[ExportacionGraficos.MaximoBytesImagen]]);

        var imagenes = ExportacionGraficos.Graficos(
            [Grafico(), Grafico(png: "%%no-es-base64%%"), Grafico(png: gif), Grafico(png: grande), Grafico(png: null), Grafico(titulo: "  " + new string('x', 200))],
            errores);

        Assert.Equal(["Mantenimientos por mes", new string('x', ExportacionGraficos.MaximoTitulo)], imagenes.Select(i => i.Titulo));
        Assert.Equal("La imagen no es válida.", errores["graficos[1]"][0]);
        Assert.Equal("Solo se aceptan imágenes PNG.", errores["graficos[2]"][0]);
        Assert.Equal("Cada imagen puede pesar hasta 2 MB.", errores["graficos[3]"][0]);
        Assert.Equal("Falta la imagen del gráfico.", errores["graficos[4]"][0]);
        Assert.Equal("Gráfico", ExportacionGraficos.Graficos([Grafico(titulo: null)], errores)[0].Titulo);
    }

    [Fact]
    public void Graficos_MasDeSeis_SeRechazan()
    {
        var errores = new Dictionary<string, string[]>();
        Assert.Empty(ExportacionGraficos.Graficos(Enumerable.Repeat(Grafico(), ExportacionGraficos.MaximoGraficos + 1).ToList(), errores));
        Assert.Contains("graficos", errores.Keys);
    }

    [Fact]
    public async Task IndicadoresPdf_ConsultaLaInstitucion_ArmaElDocumento_YLoRegistraEnLaBitacora()
    {
        IndicadoresDocumento? documento = null;
        _generador.IndicadoresPdf(Arg.Do<IndicadoresDocumento>(d => documento = d)).Returns([1, 2, 3]);

        var archivo = await Caso().IndicadoresPdfAsync(
            new SolicitudPdfIndicadores(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), 3, [Grafico()]), CancellationToken.None);

        Assert.Equal((FormatosReporte.TipoPdf, "indicadores-20261101-20261120.pdf"), (archivo.TipoContenido, archivo.NombreArchivo));
        Assert.NotNull(documento);
        Assert.Equal(75, documento!.Indicadores.CumplimientoPreventivo.Porcentaje);
        Assert.Equal(3, documento.Serie.Count);
        Assert.Equal("PL-1", documento.Parque.EquiposFuera[0].PlacaInventario);
        Assert.Single(documento.Graficos);
        Assert.Equal(ExportacionGraficos.ComoLeerIndicadores, documento.ComoLeerlo);
        await _auditoria.Received(1).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Exportar,
            ExportacionGraficos.EntidadIndicadores,
            _tenant.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["reporte"] == "indicadores" && d["desde"] == "2026-11-01" && d["graficos"] == "1"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, "meses")]
    [InlineData(13, "meses")]
    public async Task IndicadoresPdf_DatosInvalidos_NoConsulta(int meses, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().IndicadoresPdfAsync(
            new SolicitudPdfIndicadores(null, null, meses, [Grafico(png: "AAAA")]), CancellationToken.None));

        Assert.Contains(campo, error.Errores.Keys);
        Assert.Contains("graficos[0]", error.Errores.Keys);
        await _indicadores.DidNotReceiveWithAnyArgs().DatosAsync(default, default, default);
        _generador.DidNotReceiveWithAnyArgs().IndicadoresPdf(default!);
    }

    private void ConResultado()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var grupos = new List<GrupoDto>
        {
            new(0, 1, new CaracteristicasDto(2, 0.5, 1_000_000, 30), "Estables"),
            new(1, 1, new CaracteristicasDto(10, 0.8, 4_000_000, 120), "Alto costo y muchas fallas"),
        };
        var equipos = new List<(Guid, int, CaracteristicasDto)>
        {
            (a, 0, new CaracteristicasDto(2, 0.5, 1_000_000, 30)),
            (b, 1, new CaracteristicasDto(10, 0.8, 4_000_000, 120)),
        };
        _analitica.UltimoAsync(default).ReturnsForAnyArgs(new ResultadoAnalitica(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 40, 2, grupos, equipos, [new AgrupacionGuardada(2, 1.5, grupos, equipos)]));
        _placas.PlacasAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, DatosEquipo>
        {
            [a] = new("PL-A", "Bomba de infusión"),
            [b] = new("PL-B", "Ventilador"),
        });
    }

    [Fact]
    public async Task AnaliticaPdf_UsaElUltimoCalculoDeLaInstitucion_ConLosEjesElegidos()
    {
        ConResultado();
        AnaliticaDocumento? documento = null;
        _generador.AnaliticaPdf(Arg.Do<AnaliticaDocumento>(d => documento = d)).Returns([9]);

        var archivo = await Caso().AnaliticaPdfAsync(new SolicitudPdfAnalitica(2, "tiempo", "COSTO", [Grafico("Dispersión")]), CancellationToken.None);

        Assert.Equal("analitica-2-grupos.pdf", archivo.NombreArchivo);
        Assert.NotNull(documento);
        Assert.Equal(("Tiempo de atención (min)", "Costo confirmado (M COP)"), (documento!.TituloEjeX, documento.TituloEjeY));
        Assert.Equal(new FilaEquipoPdf("PL-B", "Ventilador", "Alto costo y muchas fallas", "120", "4"), documento.Equipos[0]);
        Assert.Equal(["Alto costo y muchas fallas", "Estables"], documento.Grupos.Select(g => g.Nombre));
        Assert.Equal("8", documento.Grupos[0].Fallas);
        Assert.Contains(documento.ComoLeerlo, t => t.Contains("«Alto costo y muchas fallas» reúne los equipos con más fallas", StringComparison.Ordinal));
        Assert.Contains(documento.ComoLeerlo, t => t.Contains("Inercia del modelo: 1,5", StringComparison.Ordinal));
        await _auditoria.Received(1).RegistrarAsync(
            _usuario,
            AccionesAuditoria.Exportar,
            ExportacionGraficos.EntidadAnalitica,
            _tenant.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["reporte"] == "analitica" && d["grupos"] == "2" && d["ejes"] == "tiempo/costo"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnaliticaPdf_GruposNoCalculados_O_EjeDesconocido_Responden400()
    {
        ConResultado();

        var sinCalcular = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().AnaliticaPdfAsync(new SolicitudPdfAnalitica(5, null, null, null), CancellationToken.None));
        Assert.Contains("grupos", sinCalcular.Errores.Keys);

        var eje = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().AnaliticaPdfAsync(new SolicitudPdfAnalitica(2, "edad", null, null), CancellationToken.None));
        Assert.Contains("ejeX", eje.Errores.Keys);

        var fuera = await Assert.ThrowsAsync<ErrorValidacion>(() => Caso().AnaliticaPdfAsync(new SolicitudPdfAnalitica(9, null, null, null), CancellationToken.None));
        Assert.Contains("grupos", fuera.Errores.Keys);

        _generador.DidNotReceiveWithAnyArgs().AnaliticaPdf(default!);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task AnaliticaPdf_SinResultados_Responde409ComoLaPantalla()
    {
        _analitica.UltimoAsync(default).ReturnsForAnyArgs((ResultadoAnalitica?)null);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Caso().AnaliticaPdfAsync(null, CancellationToken.None));

        Assert.Equal(ConsultaAnalitica.CodigoSinResultados, error.Codigo);
    }
}
