using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

/// <summary>
/// Calibración de un equipo (HU-022): el vencimiento es la fecha + la periodicidad (RN-010) y nunca es
/// anterior a la fecha; vigente hasta el día del vencimiento; aprobada, ya no cambia.
/// </summary>
public class RegistroCalibracionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Equipo = Guid.NewGuid();
    private static readonly Guid Tecnico = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 11, 10);

    private static RegistroCalibracion Nueva(DateOnly? vence = null, string resultado = RegistroCalibracion.Conforme) =>
        new(Tenant, Equipo, Tecnico, Fecha, "  Analizador Fluke IDA-5 ", resultado, vence ?? Fecha.AddMonths(12), "  ", Guid.Empty);

    [Theory]
    [InlineData(12, 2027, 11, 10)]
    [InlineData(1, 2026, 12, 10)]
    [InlineData(60, 2031, 11, 10)]
    public void Vencimiento_EsLaFechaMasLaPeriodicidadEnMeses(int meses, int anio, int mes, int dia) =>
        Assert.Equal(new DateOnly(anio, mes, dia), RegistroCalibracion.VencimientoPorPeriodicidad(Fecha, meses));

    [Fact]
    public void Vencimiento_ConFinDeMes_SeAjustaAlUltimoDia() =>
        Assert.Equal(new DateOnly(2027, 2, 28), RegistroCalibracion.VencimientoPorPeriodicidad(new DateOnly(2026, 8, 31), 6));

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Vencimiento_ConPeriodicidadFueraDeRango_SeRechaza(int meses) =>
        Assert.Throws<ArgumentException>(() => RegistroCalibracion.VencimientoPorPeriodicidad(Fecha, meses));

    [Fact]
    public void Nueva_LimpiaLosTextos_YQuedaSinAprobar()
    {
        var calibracion = Nueva();

        Assert.Equal(("Analizador Fluke IDA-5", (string?)null, (Guid?)null, false), (calibracion.PatronReferencia, calibracion.NumeroCertificado, calibracion.ProveedorId, calibracion.Aprobada));
        Assert.Equal((Equipo, Tecnico, Tenant), (calibracion.EquipoId, calibracion.RegistradoPorId, calibracion.TenantId));
    }

    [Fact]
    public void Vigente_HastaElDiaDelVencimientoInclusive()
    {
        var calibracion = Nueva(Fecha.AddDays(30));

        Assert.True(calibracion.EstaVigente(Fecha.AddDays(30)));
        Assert.False(calibracion.EstaVigente(Fecha.AddDays(31)));
    }

    [Fact]
    public void VencimientoAnteriorALaFecha_ResultadoDesconocido_OSinPatron_SeRechazan()
    {
        Assert.Throws<ArgumentException>(() => Nueva(Fecha.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => Nueva(resultado: "conforme con observaciones"));
        Assert.Throws<ArgumentException>(() => new RegistroCalibracion(Tenant, Equipo, Tecnico, Fecha, " ", RegistroCalibracion.Conforme, Fecha));
        Assert.Throws<ArgumentException>(() => new RegistroCalibracion(Tenant, Guid.Empty, Tecnico, Fecha, "Patrón", RegistroCalibracion.Conforme, Fecha));
    }

    [Fact]
    public void Corregir_CambiaLosDatos_YAprobada_YaNoCambia()
    {
        var calibracion = Nueva();
        var laboratorio = Guid.NewGuid();

        calibracion.Corregir(Fecha.AddDays(-1), "Simulador", RegistroCalibracion.NoConforme, Fecha.AddDays(-1), "CERT-9", laboratorio);
        Assert.Equal(("Simulador", RegistroCalibracion.NoConforme, "CERT-9", (Guid?)laboratorio), (calibracion.PatronReferencia, calibracion.Resultado, calibracion.NumeroCertificado, calibracion.ProveedorId));

        var ingeniero = Guid.NewGuid();
        calibracion.Aprobar(ingeniero);
        Assert.Equal(((Guid?)ingeniero, true), (calibracion.AprobadoPorId, calibracion.Aprobada));
        Assert.Throws<InvalidOperationException>(() => calibracion.Aprobar(ingeniero));
        Assert.Throws<InvalidOperationException>(() => calibracion.Corregir(Fecha, "Otro", RegistroCalibracion.Conforme, Fecha, null, null));
    }
}
