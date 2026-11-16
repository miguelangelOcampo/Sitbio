using System.Reflection;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Domain.Tests.Instituciones;

/// <summary>Solicitud de demostración, cotización o cambio de plan (HU-028, RF-029; V002).</summary>
public class SolicitudServicioTests
{
    private static readonly DateTimeOffset Fecha = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));

    private static SolicitudServicio Nueva(
        string? tipo = TiposSolicitudServicio.Demostracion,
        string? institucion = "Clínica San Rafael",
        string? nit = "900.123.456-7",
        string? contacto = "Ana Gómez",
        string? correo = "ana@sanrafael.co",
        string? plan = null) =>
        SolicitudServicio.DeInstitucionNueva(tipo, institucion, nit, contacto, correo, null, plan, null, Fecha);

    private static Tenant TenantConPlan(string plan)
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, Guid.NewGuid(), BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
        typeof(Tenant).GetProperty(nameof(Tenant.Plan))!.SetValue(tenant, plan, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
        tenant.ActualizarDatos("Clínica San Rafael", "900.123.456-7", 15);
        return tenant;
    }

    [Fact]
    public void DeInstitucionNueva_NaceRecibidaSinTenantYConElCorreoNormalizado()
    {
        var solicitud = Nueva(correo: "Ana@SanRafael.co", plan: "Hospital");

        Assert.Equal(EstadosSolicitudServicio.Recibida, solicitud.Estado);
        Assert.Null(solicitud.TenantId);
        Assert.Equal("ana@sanrafael.co", solicitud.Correo);
        Assert.Equal("Hospital", solicitud.PlanSolicitado);
        Assert.Equal(string.Empty, solicitud.Codigo);
        Assert.Equal(Fecha, solicitud.FechaCreacion);
    }

    [Theory]
    [InlineData(TiposSolicitudServicio.CambioPlan)]
    [InlineData("otro")]
    [InlineData(null)]
    public void DeInstitucionNueva_SoloDemostracionOCotizacion(string? tipo)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => Nueva(tipo: tipo));

        Assert.Equal("tipo", error.ParamName);
    }

    [Fact]
    public void DeInstitucionNueva_ValidaCadaCampo()
    {
        Assert.Equal("institucion", Assert.ThrowsAny<ArgumentException>(() => Nueva(institucion: "  ")).ParamName);
        Assert.Equal("institucion", Assert.ThrowsAny<ArgumentException>(() => Nueva(institucion: "IPS")).ParamName);
        Assert.Equal("nit", Assert.ThrowsAny<ArgumentException>(() => Nueva(nit: "900123456")).ParamName);
        Assert.Equal("contacto", Assert.ThrowsAny<ArgumentException>(() => Nueva(contacto: "")).ParamName);
        Assert.Equal("correo", Assert.ThrowsAny<ArgumentException>(() => Nueva(correo: "ana@demo")).ParamName);
        Assert.Equal("planSolicitado", Assert.ThrowsAny<ArgumentException>(() => Nueva(plan: "Premium")).ParamName);
    }

    [Fact]
    public void DeCambioPlan_TomaInstitucionYNitDelTenant()
    {
        var tenant = TenantConPlan(PlanServicio.Clinica);

        var solicitud = SolicitudServicio.DeCambioPlan(tenant, "Ana Gómez", "ana@sanrafael.co", null, PlanServicio.Hospital, "Abrimos sede", Fecha);

        Assert.Equal(TiposSolicitudServicio.CambioPlan, solicitud.Tipo);
        Assert.Equal(tenant.Id, solicitud.TenantId);
        Assert.Equal(tenant.Nombre, solicitud.Institucion);
        Assert.Equal(tenant.Nit, solicitud.Nit);
    }

    [Fact]
    public void DeCambioPlan_ExigeUnPlanVigenteDistintoAlActual()
    {
        var tenant = TenantConPlan(PlanServicio.Clinica);

        Assert.Equal("planSolicitado", Assert.ThrowsAny<ArgumentException>(() =>
            SolicitudServicio.DeCambioPlan(tenant, "Ana", "ana@sanrafael.co", null, PlanServicio.Clinica, null, Fecha)).ParamName);
        Assert.Equal("planSolicitado", Assert.ThrowsAny<ArgumentException>(() =>
            SolicitudServicio.DeCambioPlan(tenant, "Ana", "ana@sanrafael.co", null, null, null, Fecha)).ParamName);
    }

    [Fact]
    public void Numerar_AsignaSolConSeisDigitosUnaSolaVez()
    {
        var solicitud = Nueva();

        solicitud.Numerar(42);

        Assert.Equal("SOL-000042", solicitud.Codigo);
        Assert.Equal(42, SolicitudServicio.Consecutivo(solicitud.Codigo));
        Assert.Throws<InvalidOperationException>(() => solicitud.Numerar(43));
        Assert.Null(SolicitudServicio.Consecutivo("TQT-000001"));
    }

    [Fact]
    public void PlanesVigentes_CoincidenConV002YV003()
    {
        Assert.Equal(["Consultorio", "Clínica", "Hospital", "Red de Salud"], PlanServicio.Vigentes.Select(p => p.Nombre));
        var consultorio = PlanServicio.Buscar("Consultorio")!;
        Assert.Equal((3, 300, 5), (consultorio.MaxUsuariosGestion, consultorio.MaxEquipos, consultorio.MaxAlmacenamientoGb));
        Assert.Null(PlanServicio.Buscar("Red de Salud")!.MaxEquipos);
    }
}
