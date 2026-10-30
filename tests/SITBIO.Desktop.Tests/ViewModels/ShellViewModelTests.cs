using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public class ShellViewModelTests
{
    [Fact]
    public void AlEntrar_AbreLaPantallaDeInicioDelRolYMarcaSuModulo()
    {
        var shell = new ShellViewModel("AC-03");

        Assert.Equal("UI-012", shell.OpcionActual?.Opcion.Id);
        Assert.Equal("Mis órdenes", shell.OpcionActual?.Etiqueta);
        Assert.Equal("Inicio", shell.ModuloActual?.Corto);
        Assert.True(shell.ModuloActual!.EsActivo);
        Assert.Same(shell.ModuloActual, shell.ModuloEnPanel);
        Assert.True(shell.ModuloActual.EnPanel);
        Assert.Single(shell.Modulos, m => m.EnPanel);
        Assert.Equal("GENERAL", shell.TituloModuloActual);
    }

    [Fact]
    public void AbrirOtroModulo_CambiaElPanelPeroNoLaBarraDelActual()
    {
        var shell = new ShellViewModel("AC-02");
        var gestion = shell.Modulos.Single(m => m.Corto == "Gestión");

        shell.AbrirModuloCommand.Execute(gestion);

        Assert.Same(gestion, shell.ModuloEnPanel);
        Assert.True(gestion.EnPanel);
        Assert.False(gestion.EsActivo);
        Assert.Equal("Inicio", shell.ModuloActual?.Corto);
        Assert.Equal(["INVENTARIO", "TRABAJO", "PREVENCIÓN"], shell.GruposPanel.Select(g => g.Titulo));
    }

    [Fact]
    public void Navegar_MarcaLaOpcionYElModuloActivo()
    {
        var shell = new ShellViewModel("AC-02");
        var gestion = shell.Modulos.Single(m => m.Corto == "Gestión");
        var ordenes = gestion.Opciones.Single(o => o.Opcion.Id == "UI-012");
        var anterior = shell.OpcionActual!;

        shell.NavegarCommand.Execute(ordenes);

        Assert.True(ordenes.EsActual);
        Assert.False(anterior.EsActual);
        Assert.True(gestion.EsActivo);
        Assert.False(shell.Modulos.Single(m => m.Corto == "Inicio").EsActivo);
    }

    [Fact]
    public void Buscador_SoloConMasDeTresOpcionesYFiltraSinTildes()
    {
        var shell = new ShellViewModel("AC-01");
        Assert.False(shell.MostrarBuscador); // Admin tiene 3 opciones

        shell.AbrirModuloCommand.Execute(shell.Modulos.Single(m => m.Corto == "Gestión"));
        Assert.True(shell.MostrarBuscador);
        shell.Busqueda = "ordenes";

        var opcion = Assert.Single(shell.GruposPanel.SelectMany(g => g.Opciones));
        Assert.Equal("Órdenes de trabajo", opcion.Etiqueta);
    }

    [Fact]
    public void AlternarPanel_LoOcultaYAbrirUnModuloLoMuestra()
    {
        var shell = new ShellViewModel("AC-02");

        shell.AlternarPanelCommand.Execute(null);
        Assert.False(shell.PanelVisible);
        Assert.All(shell.Modulos, m => Assert.False(m.EnPanel));

        shell.AbrirModuloCommand.Execute(shell.Modulos[1]);
        Assert.True(shell.PanelVisible);
    }
}
