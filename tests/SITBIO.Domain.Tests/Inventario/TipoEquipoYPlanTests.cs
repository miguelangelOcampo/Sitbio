using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

public class TipoEquipoYPlanTests
{
    private static readonly DateTimeOffset Creacion = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 6);

    [Fact]
    public void TipoEquipo_ValoresPorDefecto_SinCalibracionQuedaSinPeriodicidad()
    {
        var tipo = new TipoEquipo(Guid.NewGuid(), " Estetoscopio ");

        tipo.DefinirValoresRegulatorios(ClasesRiesgo.I, false, 12);

        Assert.Equal("Estetoscopio", tipo.Nombre);
        Assert.Equal(ClasesRiesgo.I, tipo.ClaseRiesgoDefecto);
        Assert.False(tipo.RequiereCalibracion);
        Assert.Null(tipo.PeriodicidadCalibracionMeses);
    }

    [Fact]
    public void TipoEquipo_ClaseOpcionalPeroValida()
    {
        var tipo = new TipoEquipo(Guid.NewGuid(), "Tensiómetro");

        tipo.DefinirValoresRegulatorios(null, true, 12);
        Assert.Null(tipo.ClaseRiesgoDefecto);

        Assert.Throws<ArgumentException>(() => tipo.DefinirValoresRegulatorios("IV", true, 12));
        Assert.Throws<ArgumentException>(() => tipo.DefinirValoresRegulatorios(ClasesRiesgo.I, true, 61));
    }

    [Fact]
    public void Plan_ConUltimoPreventivo_ConservaLasFechas()
    {
        var ultima = Hoy.AddMonths(-2);

        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo semestral", 180, ultima.AddDays(180), Creacion, ultima);

        Assert.Equal(ultima, plan.UltimaEjecucion);
        Assert.Equal(PlanPreventivo.Activo, plan.Estado);
        Assert.False(plan.EstaVencido(Hoy));
    }

    [Fact]
    public void Plan_ProximaFechaPasada_NaceVencido()
    {
        var ultima = Hoy.AddYears(-1);

        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo semestral", 180, ultima.AddDays(180), Creacion, ultima);

        Assert.True(plan.EstaVencido(Hoy));
    }

    [Fact]
    public void Plan_UltimoNoAnteriorAlProximo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", 30, Hoy, Creacion, Hoy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Plan_PeriodicidadNoPositiva_LanzaExcepcion(int dias)
    {
        Assert.Throws<ArgumentException>(() =>
            new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", dias, Hoy, Creacion));
    }

    // ---------- HU-019: ciclo del plan ----------

    [Fact]
    public void Plan_DebeGenerarOrden_DesdeElDiaDeSuProximaFecha_YSoloActivo()
    {
        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", 30, Hoy, Creacion);

        Assert.False(plan.DebeGenerarOrden(Hoy.AddDays(-1)));
        Assert.True(plan.DebeGenerarOrden(Hoy));
        Assert.False(plan.EstaVencido(Hoy));
        Assert.True(plan.EstaVencido(Hoy.AddDays(1)));

        plan.CambiarEstado(PlanPreventivo.Suspendido);
        Assert.False(plan.DebeGenerarOrden(Hoy.AddDays(5)));
        Assert.False(plan.EstaVencido(Hoy.AddDays(5)));
    }

    [Fact]
    public void Plan_RegistrarEjecucion_UltimoEsLaFechaYProximoMasLaPeriodicidad()
    {
        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", 30, Hoy, Creacion);

        plan.RegistrarEjecucion(Hoy.AddDays(2));

        Assert.Equal(((DateOnly?)Hoy.AddDays(2), Hoy.AddDays(32)), (plan.UltimaEjecucion, plan.ProximaFecha));

        // Una fecha anterior o igual a la última no lo hace retroceder.
        plan.RegistrarEjecucion(Hoy);
        Assert.Equal(Hoy.AddDays(32), plan.ProximaFecha);
    }

    [Fact]
    public void Plan_SuspendidoNoAvanzaAlRegistrarEjecucion()
    {
        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", 30, Hoy, Creacion);
        plan.CambiarEstado(PlanPreventivo.Suspendido);

        plan.RegistrarEjecucion(Hoy);

        Assert.Equal((null, Hoy), (plan.UltimaEjecucion, plan.ProximaFecha));
    }

    [Fact]
    public void Plan_Finalizado_NoVuelveAActivarseNiCambiaSuDescripcion()
    {
        var plan = new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "Preventivo", 30, Hoy, Creacion);
        plan.CambiarEstado(PlanPreventivo.Suspendido);
        plan.CambiarEstado(PlanPreventivo.Activo);
        plan.CambiarEstado(PlanPreventivo.Finalizado);

        Assert.Throws<InvalidOperationException>(() => plan.CambiarEstado(PlanPreventivo.Activo));
        Assert.Throws<InvalidOperationException>(() => plan.CambiarDescripcion("Otra"));
        Assert.Throws<ArgumentException>(() => new PlanPreventivo(Guid.NewGuid(), Guid.NewGuid(), "P", 30, Hoy, Creacion).CambiarEstado("pausado"));
        plan.CambiarEstado(PlanPreventivo.Finalizado);
        Assert.Equal(PlanPreventivo.Finalizado, plan.Estado);
    }
}
