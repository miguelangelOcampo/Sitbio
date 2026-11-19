using NSubstitute;
using Sitbio.Desktop.Services;
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

    [Fact]
    public void Navegar_MuestraLaPantallaDeLaOpcionOElAvisoDeConstruccion()
    {
        var pantallas = Substitute.For<IFabricaPantallas>();
        var usuarios = new object();
        pantallas.Crear("UI-004").Returns(usuarios);
        var shell = new ShellViewModel("AC-01", pantallas);
        var administracion = shell.Modulos.Single(m => m.Corto == "Admin");

        shell.NavegarCommand.Execute(administracion.Opciones.Single(o => o.Opcion.Id == "UI-004"));
        Assert.Same(usuarios, shell.Contenido);
        Assert.False(shell.EnConstruccion);

        shell.NavegarCommand.Execute(administracion.Opciones.First(o => o.Opcion.Id != "UI-004"));
        Assert.Null(shell.Contenido);
        Assert.True(shell.EnConstruccion);
    }

    [Theory]
    [InlineData("AC-02")]
    [InlineData("AC-03")]
    public void IngenieroYTecnico_TienenElCentroDeAlertas_YAbrirUnaLlevaAMantenimientos(string rol)
    {
        var servicio = Substitute.For<IServicioMantenimiento>();
        var plan = Guid.NewGuid();
        servicio.AlertasAsync(Arg.Any<CancellationToken>()).Returns(new ResumenAlertas(15,
        [
            new Alerta(TiposAlerta.Preventivo, TiposAlerta.Vencida, new DateOnly(2026, 11, 1), -5, Guid.NewGuid(), "PL-001", "Bomba", "UCI", "Preventivo", plan, null, null),
        ], 0));
        var alertas = new CentroAlertasViewModel(servicio);
        alertas.CargarCommand.Execute(null);
        var mantenimientos = Substitute.For<IRecibeParametro>();
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear(CentroAlertasViewModel.IdPantalla).Returns(alertas);
        pantallas.Crear(MantenimientosViewModel.IdPantalla).Returns(mantenimientos);

        var shell = new ShellViewModel(rol, pantallas);
        Assert.Same(alertas, shell.Alertas);
        Assert.True(shell.TieneAlertas);
        Assert.Equal(1, shell.Alertas!.Pendientes);

        alertas.AbrirAlertaCommand.Execute(alertas.Filas[0]);

        Assert.Equal(MantenimientosViewModel.IdPantalla, shell.OpcionActual?.Opcion.Id);
        Assert.Same(mantenimientos, shell.Contenido);
        mantenimientos.Received(1).Recibir($"plan:{plan}");
    }

    [Theory]
    [InlineData("AC-01")]
    [InlineData("AC-04")]
    [InlineData("AC-05")]
    [InlineData("AC-07")]
    public void LosDemasRoles_NoTienenCentroDeAlertas(string rol)
    {
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear(CentroAlertasViewModel.IdPantalla).Returns(new CentroAlertasViewModel(Substitute.For<IServicioMantenimiento>()));

        var shell = new ShellViewModel(rol, pantallas);

        Assert.Equal((false, (CentroAlertasViewModel?)null), (shell.TieneAlertas, shell.Alertas));
        pantallas.DidNotReceive().Crear(CentroAlertasViewModel.IdPantalla);
    }

    [Fact]
    public void AbrirPerfil_MuestraPreferenciasSinMarcarOpcionesYCambiarContrasenaAbreUi002()
    {
        var preferencias = Substitute.For<IServicioPreferencias>();
        preferencias.Leer().Returns(new Preferencias());
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.Now.AddMinutes(30), "AC-02", false, "Laura", "Gómez"));
        var pantallas = Substitute.For<IFabricaPantallas>();
        pantallas.Crear(PerfilViewModel.IdPantalla).Returns(_ => new PerfilViewModel(sesion, preferencias, Substitute.For<ITemaAplicacion>()));
        var shell = new ShellViewModel("AC-02", pantallas, () => new CambioClaveViewModel(Substitute.For<IServicioSesion>(), obligatorio: false));
        Assert.True(shell.PuedeAbrirPerfil);

        shell.AbrirPerfilCommand.Execute("preferencias");

        var perfil = Assert.IsType<PerfilViewModel>(shell.Contenido);
        Assert.True(perfil.EnPreferencias);
        Assert.Null(shell.OpcionActual);
        Assert.DoesNotContain(shell.Modulos.SelectMany(m => m.Opciones), o => o.EsActual);
        Assert.Equal("CUENTA", shell.TituloModuloActual);

        shell.AbrirPerfilCommand.Execute("perfil");
        Assert.Same(perfil, shell.Contenido);
        Assert.True(perfil.EnPerfil);

        perfil.CambiarClaveCommand.Execute(null);
        Assert.NotNull(shell.CambioClave);
    }

    [Fact]
    public void PieDelMenu_MuestraLaVersionDelProducto()
    {
        Assert.Equal("SITBIO v1.1.1", ShellViewModel.TextoVersion(new Version(1, 1, 1, 0)));
        Assert.Equal("SITBIO v2.0.0", ShellViewModel.TextoVersion(new Version(2, 0)));
        Assert.Equal("SITBIO", ShellViewModel.TextoVersion(null));
        Assert.Equal(ShellViewModel.TextoVersion(typeof(ShellViewModel).Assembly.GetName().Version), ShellViewModel.VersionProducto);
        Assert.StartsWith("SITBIO v1.1.1", ShellViewModel.VersionProducto);
    }
}
