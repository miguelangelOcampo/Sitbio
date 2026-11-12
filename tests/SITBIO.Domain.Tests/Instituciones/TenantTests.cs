using Sitbio.Domain.Instituciones;

namespace Sitbio.Domain.Tests.Instituciones;

/// <summary>Datos que el Administrador edita de su institución (HU-024, RF-024).</summary>
public class TenantTests
{
    // Los tenants los crea el aprovisionamiento (ST-10); en la prueba se materializan como lo hace EF Core.
    private static Tenant Nuevo()
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        tenant.ActualizarDatos("Clínica San Rafael", "900.123.456-7", 15);
        return tenant;
    }

    [Fact]
    public void ActualizarDatos_GuardaLosValoresSinEspaciosALosLados()
    {
        var tenant = Nuevo();

        tenant.ActualizarDatos("  Hospital San Vicente  ", " 890.900.518-4 ", 30);

        Assert.Equal(("Hospital San Vicente", "890.900.518-4", (short)30), (tenant.Nombre, tenant.Nit, tenant.DiasAnticipacionAlerta));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("IPS")]
    public void NombreVacioOCorto_SeRechazaEnNombre(string? nombre)
    {
        var error = Assert.Throws<ArgumentException>(() => Nuevo().ActualizarDatos(nombre, "900.123.456-7", 15));

        Assert.Equal("nombre", error.ParamName);
    }

    [Fact]
    public void NombreDeMasDe150Caracteres_SeRechaza()
    {
        var error = Assert.Throws<ArgumentException>(() => Nuevo().ActualizarDatos(new string('C', 151), "900.123.456-7", 15));

        Assert.Equal("nombre", error.ParamName);
    }

    [Theory]
    [InlineData("900123456-7")]
    [InlineData("900.123.456")]
    [InlineData("900.123.456-78")]
    [InlineData("NIT 900.123.456-7")]
    [InlineData("")]
    public void NitSinElFormato_SeRechazaEnNit(string nit)
    {
        var error = Assert.Throws<ArgumentException>(() => Nuevo().ActualizarDatos("Clínica San Rafael", nit, 15));

        Assert.Equal("nit", error.ParamName);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(91)]
    [InlineData(-1)]
    public void DiasFueraDe7A90_SeRechazanSinCambiarNada(int dias)
    {
        var tenant = Nuevo();

        var error = Assert.Throws<ArgumentException>(() => tenant.ActualizarDatos("Otro nombre de IPS", "800.000.000-1", dias));

        Assert.Equal("diasAnticipacionAlerta", error.ParamName);
        Assert.Equal(("Clínica San Rafael", "900.123.456-7", (short)15), (tenant.Nombre, tenant.Nit, tenant.DiasAnticipacionAlerta));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(90)]
    public void DiasEnLosLimites_SeAceptan(int dias)
    {
        var tenant = Nuevo();

        tenant.ActualizarDatos("Clínica San Rafael", "900.123.456-7", dias);

        Assert.Equal(dias, tenant.DiasAnticipacionAlerta);
    }
}
