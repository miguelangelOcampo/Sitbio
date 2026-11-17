using NSubstitute;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

public class PerfilViewModelTests
{
    private readonly IServicioPreferencias _preferencias = Substitute.For<IServicioPreferencias>();
    private readonly ITemaAplicacion _tema = Substitute.For<ITemaAplicacion>();

    public PerfilViewModelTests() => _preferencias.Leer().Returns(new Preferencias());

    private PerfilViewModel Crear(string nombres = "Laura María", string apellidos = "Gómez Ruiz", string correo = "laura.gomez@hsv.co")
    {
        var sesion = new SesionActual();
        sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.Now.AddMinutes(30), "AC-02", false, nombres, apellidos, "Hospital San Vicente", correo));
        return new PerfilViewModel(sesion, _preferencias, _tema);
    }

    [Fact]
    public void MuestraLosDatosDeLaCuentaDeLaSesion()
    {
        var vm = Crear();

        Assert.Equal("Laura María Gómez Ruiz", vm.NombreCompleto);
        Assert.Equal("LG", vm.Iniciales);
        Assert.Equal("laura.gomez@hsv.co", vm.Correo);
        Assert.Equal("AC-02", vm.CodigoRol);
        Assert.Equal(GuiaRoles.NombreDe("AC-02"), vm.NombreRol);
        Assert.Equal("Hospital San Vicente", vm.Institucion);
    }

    [Fact]
    public void SinNombreNiCorreo_MuestraTextosDeRespaldo()
    {
        var vm = Crear("", "", "");

        Assert.Equal("Usuario de SITBIO", vm.NombreCompleto);
        Assert.Equal("?", vm.Iniciales);
        Assert.Equal("—", vm.Correo);
    }

    [Fact]
    public void Pestanas_SonExcluyentesYCambianElTitulo()
    {
        var vm = Crear();
        Assert.True(vm.EnPerfil);
        Assert.Equal("Mi perfil", vm.Titulo);

        vm.Mostrar(preferencias: true);

        Assert.False(vm.EnPerfil);
        Assert.True(vm.EnPreferencias);
        Assert.Equal("Preferencias", vm.Titulo);
    }

    [Fact]
    public void ElegirOscuro_LoAplicaAlInstanteYLoGuarda()
    {
        var vm = Crear();
        Assert.True(vm.EsClaro);

        vm.EsOscuro = true;

        Assert.True(vm.EsOscuro);
        Assert.False(vm.EsClaro);
        _tema.Received(1).Aplicar(Temas.Oscuro);
        _preferencias.Received(1).Guardar(new Preferencias(Temas.Oscuro));
    }

    [Fact]
    public void ElegirElTemaQueYaEstaActivo_NoHaceNada()
    {
        var vm = Crear();

        vm.EsClaro = true;
        vm.EsOscuro = false;

        _tema.DidNotReceiveWithAnyArgs().Aplicar(default!);
        _preferencias.DidNotReceiveWithAnyArgs().Guardar(default!);
    }

    [Fact]
    public void ArrancaConElTemaGuardado()
    {
        _preferencias.Leer().Returns(new Preferencias(Temas.Oscuro));

        var vm = Crear();

        Assert.True(vm.EsOscuro);
    }

    [Fact]
    public void CambiarContrasena_AvisaAlMarco()
    {
        var vm = Crear();
        var avisado = false;
        vm.CambioClaveSolicitado += (_, _) => avisado = true;

        vm.CambiarClaveCommand.Execute(null);

        Assert.True(avisado);
    }
}
