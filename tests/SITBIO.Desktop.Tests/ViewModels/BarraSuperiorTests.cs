using NSubstitute;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// H-03: barra superior como en el prototipo. El avatar muestra las iniciales del usuario, el pie del panel su nombre
/// y cada rol con acción principal la tiene a la derecha, salvo cuando ya está en esa pantalla.
/// </summary>
public class BarraSuperiorTests
{
    private static SesionUsuario Usuario(string nombres, string apellidos, string correo = "") =>
        new("a.b.c", DateTimeOffset.Now.AddMinutes(30), "AC-02", false, nombres, apellidos, "HSV", correo);

    [Theory]
    [InlineData("Laura Gómez", "LG")]
    [InlineData("  ana  maría   ruiz pérez ", "AP")]
    [InlineData("Carlos", "C")]
    [InlineData("", "?")]
    public void Iniciales_PrimeraYUltimaPalabra(string nombre, string esperado) =>
        Assert.Equal(esperado, ShellViewModel.InicialesDe(nombre));

    [Fact]
    public void NombreYCorreo_VienenDeLaSesion()
    {
        var shell = new ShellViewModel("AC-02") { Usuario = Usuario("Laura", "Gómez", "laura@hsv.test") };

        Assert.Equal("Laura Gómez", shell.NombreUsuario);
        Assert.Equal("laura@hsv.test", shell.CorreoUsuario);
        Assert.Equal("LG", shell.Iniciales);
    }

    [Fact]
    public void Iniciales_NombreYPrimerApellido()
    {
        var shell = new ShellViewModel("AC-02") { Usuario = Usuario("Laura María", "Gómez Ruiz") };

        Assert.Equal("LG", shell.Iniciales);
    }

    [Fact]
    public void Miga_ModuloYPantallaActual()
    {
        var shell = new ShellViewModel("AC-02");

        Assert.Equal(shell.ModuloActual?.Nombre, shell.GrupoMiga);
        Assert.Equal(shell.OpcionActual?.Etiqueta, shell.TituloMiga);
        Assert.False(string.IsNullOrEmpty(shell.TituloMiga));
    }

    [Theory]
    [InlineData("AC-05", true)]
    [InlineData("AC-06", true)]
    [InlineData("AC-07", true)]
    [InlineData("AC-02", false)]
    [InlineData("AC-04", false)]
    public void RolesDeConsulta_MuestranSoloLectura(string rol, bool esperado) =>
        Assert.Equal(esperado, new ShellViewModel(rol).SoloLectura);

    [Fact]
    public void SinNombre_UsaElNombreDelRol()
    {
        var shell = new ShellViewModel("AC-02") { Usuario = Usuario("", "") };

        Assert.Equal(shell.NombreRol, shell.NombreUsuario);
        Assert.Equal(string.Empty, new ShellViewModel("AC-02").CorreoUsuario);
    }

    [Theory]
    [InlineData("AC-02", "Registrar equipo", "UI-005")]
    [InlineData("AC-04", "Reportar falla", "UI-009")]
    [InlineData("AC-01", "Usuarios", "UI-004")]
    public void CadaRolConAccion_LaTieneEnLaBarra(string rol, string texto, string destino)
    {
        var shell = new ShellViewModel(rol);

        Assert.Equal(texto, shell.AccionPrincipal?.Texto);
        Assert.Equal(destino, shell.AccionPrincipal?.Destino);
    }

    [Theory]
    [InlineData("AC-03")]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData("AC-07")]
    public void LosDemasRoles_NoTienenAccionPrincipal(string rol)
    {
        var shell = new ShellViewModel(rol);

        Assert.Null(shell.AccionPrincipal);
        Assert.False(shell.MostrarAccionPrincipal);
    }

    [Fact]
    public void LaAccion_NavegaASuPantalla_YSeOcultaEstandoEnElla()
    {
        var pantallas = Substitute.For<IFabricaPantallas>();
        var equipos = Substitute.For<IRecibeParametro>();
        pantallas.Crear("UI-005").Returns(equipos);
        var shell = new ShellViewModel("AC-02", pantallas);
        Assert.NotEqual("UI-005", shell.OpcionActual?.Opcion.Id);
        Assert.True(shell.MostrarAccionPrincipal);

        shell.EjecutarAccionPrincipalCommand.Execute(null);

        Assert.Equal("UI-005", shell.OpcionActual?.Opcion.Id);
        equipos.Received(1).Recibir(EquiposViewModel.ParametroNuevo);
        Assert.False(shell.MostrarAccionPrincipal);
    }
}
