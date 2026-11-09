using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

public class DetalleEquipoViewModelTests
{
    private readonly IServicioEquipos _servicio = Substitute.For<IServicioEquipos>();
    private int _vueltas;

    private async Task<DetalleEquipoViewModel> AbrirAsync(Equipo equipo)
    {
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        var detalle = new DetalleEquipoViewModel(_servicio, new Reloj(), equipo.Id, equipo.PlacaInventario, () => _vueltas++);
        await detalle.CargarCommand.ExecuteAsync(null);
        return detalle;
    }

    private static Dato DatoDe(IEnumerable<Dato> datos, string etiqueta) => datos.Single(d => d.Etiqueta == etiqueta);

    [Fact]
    public async Task Encabezado_MuestraTipoEstadoYSubtitulo()
    {
        var detalle = await AbrirAsync(Equipo());

        Assert.Equal("Monitor de signos vitales", detalle.Titulo);
        Assert.Equal("Clase IIb · Philips IntelliVue MX450 · UCI adultos", detalle.Subtitulo);
        Assert.Equal("Operativo", detalle.Estado);
        Assert.Equal(TonoEstado.Exito, detalle.Tono);
        Assert.Null(detalle.AvisoRegulatorio);
        Assert.True(detalle.EnResumen);
    }

    [Fact]
    public async Task Indicadores_MuestranMantenimientoCalibracionYAutorizacion()
    {
        var detalle = await AbrirAsync(Equipo(plan: Plan(Hoy.AddDays(4))));

        Assert.Equal("10/11/2026", detalle.ProximoMantenimiento);
        Assert.Equal("en 4 días · trimestral", detalle.DetalleMantenimiento);
        Assert.Equal("Cada 12 meses", detalle.Calibracion);
        Assert.Equal("Vigente", detalle.Autorizacion);
        Assert.Equal("Hasta 01/01/2028", detalle.DetalleAutorizacion);
        Assert.False(detalle.AutorizacionEnAlerta);
    }

    [Fact]
    public async Task SinPlanPreventivo_LoDiceSinInventarFechas()
    {
        var detalle = await AbrirAsync(Equipo());

        Assert.Equal("Sin plan", detalle.ProximoMantenimiento);
        Assert.Equal("No se conoce", DatoDe(detalle.InformacionTecnica, "Último preventivo").Valor);
    }

    [Fact]
    public async Task RN010_RegistroVencido_AvisaSinBloquear()
    {
        var detalle = await AbrirAsync(Equipo(avisoRegistro: AvisosRegistro.Vencido, venceRegistro: new DateOnly(2026, 10, 1)));

        Assert.Equal("Autorización INVIMA vencida", detalle.AvisoRegulatorio!.Titulo);
        Assert.Contains("INVIMA 2019DM-0019830 venció el 01/10/2026", detalle.AvisoRegulatorio.Mensaje);
        Assert.Equal("RN-010", detalle.AvisoRegulatorio.Codigo);
        Assert.Equal("Vencida", detalle.Autorizacion);
        Assert.True(detalle.AutorizacionEnAlerta);
    }

    [Fact]
    public async Task RN010_RegistroPorVencer_EsAdvertenciaConLosDiasQueFaltan()
    {
        var detalle = await AbrirAsync(Equipo(avisoRegistro: AvisosRegistro.PorVencer, venceRegistro: Hoy.AddDays(96)));

        Assert.Equal("Autorización INVIMA por vencer", detalle.AvisoRegulatorio!.Titulo);
        Assert.Contains("en 96 días", detalle.AvisoRegulatorio.Mensaje);
        Assert.False(detalle.AvisoRegulatorio.EsError);
    }

    [Fact]
    public async Task RN010_SinCalibracion_NoAplica()
    {
        var detalle = await AbrirAsync(Equipo(clase: "I", calibra: false));

        Assert.Equal("No aplica", detalle.Calibracion);
        Assert.Equal("No", DatoDe(detalle.InformacionRegulatoria, "Requiere calibración").Valor);
        Assert.Equal("I · Riesgo bajo", DatoDe(detalle.InformacionRegulatoria, "Clase de riesgo").Valor);
    }

    [Fact]
    public async Task EquipoDeBaja_AvisaLaBajaYNoElRegistro()
    {
        var detalle = await AbrirAsync(Equipo(estado: "BAJA", avisoRegistro: AvisosRegistro.Vencido, baja: new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero)));

        Assert.Null(detalle.AvisoRegulatorio);
        Assert.Equal("Equipo dado de baja el 01/09/2026", detalle.AvisoBaja!.Titulo);
    }

    [Fact]
    public async Task DatosDelActivo_UsanLetraMonoespaciadaEnLosCodigos()
    {
        var detalle = await AbrirAsync(Equipo());

        Assert.Equal(9, detalle.DatosActivo.Count);
        Assert.True(DatoDe(detalle.DatosActivo, "Código de activo").Codigo);
        Assert.False(DatoDe(detalle.DatosActivo, "Fabricante").Codigo);
        Assert.Equal("—", DatoDe(detalle.InformacionTecnica, "Proveedor").Valor);
    }

    [Fact]
    public async Task Pestanas_SonExcluyentes()
    {
        var detalle = await AbrirAsync(Equipo());

        detalle.EnTecnica = true;

        Assert.False(detalle.EnResumen);
    }

    [Fact]
    public async Task NoEncontrado_MuestraAvisoConReintentar()
    {
        var id = Guid.NewGuid();
        _servicio.ObtenerAsync(id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "El equipo no existe o no pertenece a tu institución.", new Dictionary<string, string>()));
        var detalle = new DetalleEquipoViewModel(_servicio, new Reloj(), id, "HSV-EQ-0412", () => _vueltas++);

        await detalle.CargarCommand.ExecuteAsync(null);

        Assert.False(detalle.HayEquipo);
        Assert.Equal("No pudimos abrir la hoja de vida", detalle.Aviso!.Titulo);
        Assert.True(detalle.Aviso.PuedeReintentar);
        Assert.Equal("HSV-EQ-0412", detalle.Titulo);
    }

    [Fact]
    public async Task Volver_RegresaALaLista()
    {
        var detalle = await AbrirAsync(Equipo());

        detalle.VolverCommand.Execute(null);

        Assert.Equal(1, _vueltas);
    }
}
