using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>«Conoce los planes» desde UI-001 (HU-028): planes vigentes y solicitud sin cuenta (MSG-021).</summary>
public class SolicitudServicioViewModelTests
{
    private readonly IServicioSolicitudesServicio _servicio = Substitute.For<IServicioSolicitudesServicio>();
    private readonly IServicioAutenticacion _autenticacion = Substitute.For<IServicioAutenticacion>();

    private static readonly IReadOnlyList<PlanOfrecido> Planes =
    [
        new("Consultorio", 3, 300, 5, 190_000m),
        new("Clínica", 7, 1000, 25, 380_000m),
        new("Hospital", 14, 3000, 100, 800_000m),
        new("Red de Salud", null, null, null, null),
    ];

    private SolicitudServicioViewModel Modelo()
    {
        _servicio.PlanesAsync(Arg.Any<CancellationToken>()).Returns(Planes);
        return new SolicitudServicioViewModel(_servicio)
        {
            Institucion = "Clínica San Rafael",
            Nit = "900.123.456-7",
            Contacto = "Ana Gómez",
            Correo = "ana@sanrafael.co",
        };
    }

    [Fact]
    public async Task ConocerPlanes_DesdeElLogin_AbreElPanelConLosPlanesYLoCierra()
    {
        var login = new LoginViewModel(_autenticacion, Modelo);
        Assert.True(login.PuedeConocerPlanes);

        await login.ConocerPlanesCommand.ExecuteAsync(null);

        Assert.NotNull(login.Solicitud);
        Assert.Equal(4, login.Solicitud.Planes.Count);
        Assert.Equal(new PlanVisible("Clínica", "7 usuarios de gestión · 1.000 equipos · 25 GB", "$380.000", "COP/mes"), login.Solicitud.Planes[1]);
        Assert.Equal("Cotización", login.Solicitud.Planes[3].Valor);
        Assert.Equal([SolicitudServicioViewModel.SinPlan, "Consultorio", "Clínica", "Hospital", "Red de Salud"], login.Solicitud.OpcionesPlan);

        login.Solicitud.CerrarCommand.Execute(null);
        Assert.Null(login.Solicitud);
    }

    [Fact]
    public void SinFabrica_ElLoginNoOfreceLosPlanes()
    {
        Assert.False(new LoginViewModel(_autenticacion).PuedeConocerPlanes);
    }

    [Fact]
    public async Task CA_HU_028_01_Enviar_MuestraElCodigoDeLaSolicitud()
    {
        var vm = Modelo();
        vm.EsCotizacion = true;
        vm.Plan = "Hospital";
        vm.Telefono = "  ";
        _servicio.EnviarAsync(Arg.Any<DatosSolicitud>(), Arg.Any<CancellationToken>())
            .Returns(new SolicitudEnviada("SOL-000012", "cotizacion", "recibida", "Hospital", DateTimeOffset.UtcNow));

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.True(vm.Enviada);
        Assert.Equal("SOL-000012", vm.CodigoEnviado);
        await _servicio.Received(1).EnviarAsync(
            new DatosSolicitud("cotizacion", "Clínica San Rafael", "900.123.456-7", "Ana Gómez", "ana@sanrafael.co", null, "Hospital", null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinPlanElegido_SeEnviaDemostracionSinPlan()
    {
        var vm = Modelo();
        _servicio.EnviarAsync(Arg.Any<DatosSolicitud>(), Arg.Any<CancellationToken>())
            .Returns(new SolicitudEnviada("SOL-000013", "demostracion", "recibida", null, DateTimeOffset.UtcNow));

        await vm.EnviarCommand.ExecuteAsync(null);

        await _servicio.Received(1).EnviarAsync(Arg.Is<DatosSolicitud>(d => d.Tipo == "demostracion" && d.PlanSolicitado == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "900.123.456-7", "Ana", "ana@sanrafael.co")]
    [InlineData("Clínica", "900123456", "Ana", "ana@sanrafael.co")]
    [InlineData("Clínica", "900.123.456-7", "", "ana@sanrafael.co")]
    [InlineData("Clínica", "900.123.456-7", "Ana", "ana@sanrafael")]
    public async Task DatosInvalidos_NoLlamaALaApiYMarcaElCampo(string institucion, string nit, string contacto, string correo)
    {
        var vm = Modelo();
        vm.Institucion = institucion;
        vm.Nit = nit;
        vm.Contacto = contacto;
        vm.Correo = correo;

        await vm.EnviarCommand.ExecuteAsync(null);

        await _servicio.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        Assert.False(vm.Enviada);
        Assert.True(vm.ErrorInstitucion is not null || vm.ErrorNit is not null || vm.ErrorContacto is not null || vm.ErrorCorreo is not null);
    }

    [Fact]
    public async Task ErrorPorCampoDeLaApi_SeUbicaEnElCampo()
    {
        var vm = Modelo();
        _servicio.EnviarAsync(Arg.Any<DatosSolicitud>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, "Usa el formato 900.000.000-0.", new Dictionary<string, string> { ["nit"] = "Usa el formato 900.000.000-0." }));

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal("Usa el formato 900.000.000-0.", vm.ErrorNit);
        Assert.Null(vm.Aviso);
    }

    [Fact]
    public async Task SinConexion_MuestraMsg011YConservaLosDatos()
    {
        var vm = Modelo();
        _servicio.EnviarAsync(Arg.Any<DatosSolicitud>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));

        await vm.EnviarCommand.ExecuteAsync(null);

        Assert.Equal("MSG-011", vm.Aviso?.Codigo);
        Assert.Equal("Clínica San Rafael", vm.Institucion);
        Assert.False(vm.Enviada);
    }
}
