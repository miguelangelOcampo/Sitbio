using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

public class EquipoBiomedicoTests
{
    private static readonly DateTimeOffset Creacion = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 6);

    private static EquipoBiomedico NuevoEquipo(string placa = "PL-001", string serie = "SN-001") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serie, placa, "Mindray", "BeneView T5", Creacion);

    [Fact]
    public void Crear_NaceOperativoYConLosDatosSinEspacios()
    {
        var equipo = NuevoEquipo(placa: "  PL-001 ", serie: " SN-001");

        Assert.Equal(EstadosEquipo.Operativo, equipo.EstadoEquipoId);
        Assert.Equal("PL-001", equipo.PlacaInventario);
        Assert.Equal("SN-001", equipo.NumeroSerie);
        Assert.False(equipo.DadoDeBaja);
        Assert.Equal(AvisoRegistro.SinDato, equipo.AvisoDelRegistro(Hoy));
    }

    [Theory]
    [InlineData("", "SN-1")]
    [InlineData("PL-1", "  ")]
    public void Crear_SinPlacaOSerie_LanzaExcepcion(string placa, string serie)
    {
        Assert.Throws<ArgumentException>(() => NuevoEquipo(placa, serie));
    }

    [Fact]
    public void Crear_PlacaMasLargaQueLaColumna_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => NuevoEquipo(placa: new string('P', EquipoBiomedico.LongitudMaximaPlaca + 1)));
    }

    [Fact]
    public void Crear_SinTipoOUbicacion_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            new EquipoBiomedico(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "SN", "PL", "M", "Mo", Creacion));
        Assert.Throws<ArgumentException>(() =>
            new EquipoBiomedico(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "SN", "PL", "M", "Mo", Creacion));
    }

    [Fact]
    public void InformacionRegulatoria_Tensiometro_ClaseIConCalibracion()
    {
        var equipo = NuevoEquipo();

        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "INVIMA 2020DM-0012345", Hoy.AddYears(3), ClasesRiesgo.I, true, 12);

        Assert.Equal(ClasesRiesgo.I, equipo.ClaseRiesgo);
        Assert.True(equipo.RequiereCalibracion);
        Assert.Equal((short)12, equipo.PeriodicidadCalibracionMeses);
    }

    [Fact]
    public void InformacionRegulatoria_EstetoscopioSinCalibracion_GuardaPeriodicidadVacia()
    {
        var equipo = NuevoEquipo();

        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.PermisoComercializacion, "PC-2021-777", Hoy.AddYears(2), ClasesRiesgo.I, false, 12);

        Assert.False(equipo.RequiereCalibracion);
        Assert.Null(equipo.PeriodicidadCalibracionMeses);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(null)]
    public void InformacionRegulatoria_CalibracionConPeriodicidadFueraDeRango_LanzaExcepcion(int? meses)
    {
        var equipo = NuevoEquipo();

        Assert.Throws<ArgumentException>(() =>
            equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", Hoy, ClasesRiesgo.IIa, true, meses));
    }

    [Theory]
    [InlineData("IV")]
    [InlineData("iia")]
    [InlineData("")]
    public void InformacionRegulatoria_ClaseNoValida_LanzaExcepcion(string clase)
    {
        Assert.Throws<ArgumentException>(() =>
            NuevoEquipo().DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", Hoy, clase, false, null));
    }

    [Fact]
    public void InformacionRegulatoria_TipoDeAutorizacionNoValido_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            NuevoEquipo().DefinirInformacionRegulatoria("licencia", "R-1", Hoy, ClasesRiesgo.III, false, null));
    }

    [Fact]
    public void InformacionRegulatoria_FallaSinCambiarLosDatosAnteriores()
    {
        var equipo = NuevoEquipo();
        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", Hoy.AddYears(1), ClasesRiesgo.IIb, true, 6);

        Assert.Throws<ArgumentException>(() =>
            equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-2", Hoy, ClasesRiesgo.IIb, true, 0));

        Assert.Equal("R-1", equipo.RegistroInvima);
        Assert.Equal((short)6, equipo.PeriodicidadCalibracionMeses);
    }

    [Theory]
    [InlineData(-1, AvisoRegistro.Vencido)]
    [InlineData(0, AvisoRegistro.PorVencer)]
    [InlineData(180, AvisoRegistro.PorVencer)]
    [InlineData(181, AvisoRegistro.Vigente)]
    public void AvisoDelRegistro_SegunLosDiasQueFaltan(int dias, AvisoRegistro esperado)
    {
        var equipo = NuevoEquipo();
        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", Hoy.AddDays(dias), ClasesRiesgo.IIa, false, null);

        Assert.Equal(esperado, equipo.AvisoDelRegistro(Hoy));
    }
}
