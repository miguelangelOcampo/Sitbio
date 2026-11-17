using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public class MenuPorRolTests
{
    public static TheoryData<string> Roles => new() { "AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-06", "AC-07" };

    [Theory]
    [MemberData(nameof(Roles))]
    public void CadaRol_TieneModulosYSuPantallaDeInicioEstaEnElMenu(string rol)
    {
        var modulos = MenuPorRol.Para(rol);

        Assert.NotEmpty(modulos);
        Assert.All(modulos, m => Assert.NotEmpty(m.Opciones));
        Assert.Contains(MenuPorRol.Inicio(rol), modulos.SelectMany(m => m.Opciones).Select(o => o.Id));
    }

    [Fact]
    public void Administrador_EntraAlPanelYLuegoVeAdminConUsuariosYRoles()
    {
        var modulos = MenuPorRol.Para("AC-01");

        Assert.Equal("Inicio", modulos[0].Corto);
        Assert.Equal("Admin", modulos[1].Corto);
        Assert.Contains(modulos[1].Opciones, o => o.Id == "UI-004" && o.Etiqueta == "Usuarios y roles");
        Assert.Equal("UI-003", MenuPorRol.Inicio("AC-01"));
    }

    [Theory]
    [InlineData("AC-01", "UI-003")]
    [InlineData("AC-02", "UI-003")]
    [InlineData("AC-03", "UI-012")]
    [InlineData("AC-04", "UI-009")]
    [InlineData("AC-05", "UI-003")]
    [InlineData("AC-06", "UI-003")]
    [InlineData("AC-07", "UI-003")]
    public void CadaRol_EntraASuPantallaDeInicio(string rol, string inicio)
    {
        Assert.Equal(inicio, MenuPorRol.Inicio(rol));
    }

    [Fact]
    public void PersonalClinico_NoVeAdministracionNiAnalisis()
    {
        var cortos = MenuPorRol.Para("AC-04").Select(m => m.Corto).ToList();

        Assert.Equal(["Inicio", "Gestión"], cortos);
    }

    [Fact]
    public void Directiva_SoloVeElPanelYAnalisis()
    {
        var cortos = MenuPorRol.Para("AC-06").Select(m => m.Corto).ToList();

        Assert.Equal(["Inicio", "Análisis"], cortos);
    }

    [Fact]
    public void Gestion_AgrupaEnInventarioTrabajoYPrevencion()
    {
        var gestion = MenuPorRol.Para("AC-02").Single(m => m.Corto == "Gestión");

        Assert.All(gestion.Opciones, o => Assert.Contains(o.Subgrupo, MenuPorRol.OrdenSubgrupos));
        Assert.Equal(["Inventario", "Trabajo", "Prevención"], gestion.Opciones.Select(o => o.Subgrupo).Distinct());
    }

    [Fact]
    public void RolDesconocido_NoVeNada()
    {
        Assert.Empty(MenuPorRol.Para("AC-99"));
        Assert.Null(MenuPorRol.Inicio("AC-99"));
    }
}
