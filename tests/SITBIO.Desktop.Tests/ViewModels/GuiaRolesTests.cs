using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>La guía de UI-004 coincide con los roles vigentes (V006) y con RN-009.</summary>
public class GuiaRolesTests
{
    [Fact]
    public void TieneLosSieteRolesEnOrden()
    {
        Assert.Equal(["AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-06", "AC-07"], GuiaRoles.Roles.Select(r => r.Codigo));
        Assert.Equal("Gerente de Tecnología Biomédica", GuiaRoles.NombreDe("AC-05"));
    }

    [Theory]
    [InlineData("AC-01", true, "cuenta en el plan")]
    [InlineData("AC-02", true, "cuenta en el plan")]
    [InlineData("AC-03", true, "cuenta en el plan")]
    [InlineData("AC-04", false, "sin límite")]
    [InlineData("AC-05", true, "cuenta en el plan")]
    [InlineData("AC-06", true, "cuenta en el plan")]
    [InlineData("AC-07", false, "temporal, no cuenta")]
    public void Rn009_SoloLosRolesDeGestionCuentanEnElPlan(string codigo, bool cuenta, string limite)
    {
        var rol = GuiaRoles.Buscar(codigo)!;

        Assert.Equal(cuenta, rol.CuentaEnPlan);
        Assert.Equal(limite, rol.Limite);
    }

    [Fact]
    public void LaMatrizTieneUnPermisoPorRol()
    {
        Assert.All(GuiaRoles.Matriz, fila => Assert.Equal(7, fila.PorRol.Count));
    }

    [Fact]
    public void AjusteDel0111_CamposDinamicosYCatalogosLosGestionaAc02()
    {
        var campos = GuiaRoles.Matriz.Single(f => f.Modulo == "Campos dinámicos").PorRol;
        var catalogos = GuiaRoles.Matriz.Single(f => f.Modulo == "Catálogos técnicos").PorRol;

        Assert.Equal(NivelPermiso.Consulta, GuiaRoles.Nivel(campos[0]));
        Assert.Equal(NivelPermiso.Gestiona, GuiaRoles.Nivel(campos[1]));
        Assert.Equal(NivelPermiso.Consulta, GuiaRoles.Nivel(catalogos[0]));
        Assert.Equal(NivelPermiso.Gestiona, GuiaRoles.Nivel(catalogos[1]));
    }

    [Theory]
    [InlineData("Sin acceso", NivelPermiso.Ninguno)]
    [InlineData("Solo consulta", NivelPermiso.Consulta)]
    [InlineData("Consultar KPIs", NivelPermiso.Consulta)]
    [InlineData("Consultar e importar", NivelPermiso.Gestiona)]
    [InlineData("Crear/Editar", NivelPermiso.Gestiona)]
    public void Nivel_ClasificaElPermiso(string permiso, NivelPermiso esperado)
    {
        Assert.Equal(esperado, GuiaRoles.Nivel(permiso));
    }

    [Fact]
    public void ElMenuDeCadaRolEsElDelShell()
    {
        var administrador = GuiaRoles.Buscar("AC-01")!;

        Assert.Contains("Usuarios y roles", administrador.Menu);
        Assert.DoesNotContain("Usuarios y roles", GuiaRoles.Buscar("AC-02")!.Menu);
    }
}
