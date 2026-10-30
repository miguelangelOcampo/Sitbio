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
    public void Administrador_EmpiezaEnAdminConUsuariosYRoles()
    {
        var admin = MenuPorRol.Para("AC-01")[0];

        Assert.Equal("Admin", admin.Corto);
        Assert.Contains(admin.Opciones, o => o.Id == "UI-004" && o.Etiqueta == "Usuarios y roles");
        Assert.Equal("UI-004", MenuPorRol.Inicio("AC-01"));
    }

    [Fact]
    public void PersonalClinico_NoVeAdministracionNiAnalisis()
    {
        var cortos = MenuPorRol.Para("AC-04").Select(m => m.Corto).ToList();

        Assert.Equal(["Inicio", "Gestión"], cortos);
    }

    [Fact]
    public void Directiva_SoloVeAnalisis()
    {
        var modulo = Assert.Single(MenuPorRol.Para("AC-06"));

        Assert.Equal("Análisis", modulo.Corto);
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
