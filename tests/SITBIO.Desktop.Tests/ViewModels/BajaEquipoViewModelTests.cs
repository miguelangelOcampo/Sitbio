using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>HU-014 en la hoja de vida (UI-006): diálogo de baja con motivo y reversión de la baja.</summary>
public class BajaEquipoViewModelTests
{
    private static readonly DateTimeOffset FechaBaja = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    private readonly IServicioEquipos _servicio = Substitute.For<IServicioEquipos>();
    private int _refrescos;

    private async Task<DetalleEquipoViewModel> AbrirAsync(Equipo equipo, bool puedeEditar = true)
    {
        _servicio.ObtenerAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(equipo);
        var detalle = new DetalleEquipoViewModel(
            _servicio, new Reloj(), equipo.Id, equipo.PlacaInventario, () => { }, puedeEditar, () =>
            {
                _refrescos++;
                return Task.CompletedTask;
            });
        await detalle.CargarCommand.ExecuteAsync(null);
        return detalle;
    }

    private static Equipo DeBaja() => Equipo(estado: EstadosEquipo.Baja, baja: FechaBaja);

    // ---------- Permisos (RNF-003: la API también lo valida) ----------

    [Fact]
    public async Task IngenieroConEquipoActivo_VeDarDeBajaYNoRevertir()
    {
        var detalle = await AbrirAsync(Equipo());

        Assert.True(detalle.PuedeDarDeBaja);
        Assert.False(detalle.PuedeRevertir);
        Assert.True(detalle.PuedeEditar);
    }

    [Fact]
    public async Task IngenieroConEquipoDeBaja_VeRevertirYNoEditar()
    {
        var detalle = await AbrirAsync(DeBaja());

        Assert.False(detalle.PuedeDarDeBaja);
        Assert.False(detalle.PuedeEditar);
        Assert.True(detalle.PuedeRevertir);
        Assert.NotNull(detalle.AvisoBaja);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolesDeConsulta_NoVenNiBajaNiReversion(bool deBaja)
    {
        var detalle = await AbrirAsync(deBaja ? DeBaja() : Equipo(), puedeEditar: false);

        detalle.AbrirBajaCommand.Execute(null);
        detalle.AbrirReversionCommand.Execute(null);

        Assert.False(detalle.PuedeDarDeBaja);
        Assert.False(detalle.PuedeRevertir);
        Assert.Null(detalle.Baja);
    }

    // ---------- Dar de baja ----------

    [Fact]
    public async Task AbrirBaja_MuestraLosMotivosDelPrototipo()
    {
        var detalle = await AbrirAsync(Equipo());

        detalle.AbrirBajaCommand.Execute(null);

        var dialogo = detalle.Baja!;
        Assert.Equal("Dar de baja Monitor de signos vitales HSV-EQ-0412", dialogo.Titulo);
        Assert.Equal(
            ["Obsolescencia tecnológica", "Daño irreparable", "Fin de vida útil", "Traslado o donación", "Otro"],
            dialogo.Opciones.Select(o => o.Texto));
        Assert.Equal("Dar de baja", dialogo.TextoConfirmar);
        Assert.Equal("Observación (opcional)", dialogo.EtiquetaObservacion);
    }

    [Fact]
    public async Task SinMotivo_MarcaElCampoConMsg008YNoEnvia()
    {
        var detalle = await AbrirAsync(Equipo());
        detalle.AbrirBajaCommand.Execute(null);

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Equal("Elige el motivo de la baja.", detalle.Baja!.ErrorMotivo);
        Assert.Equal("MSG-008", detalle.Baja.Aviso!.Codigo);
        await _servicio.DidNotReceiveWithAnyArgs().CambiarEstadoAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  corto   ")]
    public async Task Otro_ExigeDescribirloConAlMenos10Caracteres(string observacion)
    {
        var detalle = await AbrirAsync(Equipo());
        detalle.AbrirBajaCommand.Execute(null);
        var dialogo = detalle.Baja!;
        dialogo.Opciones.Single(o => o.Texto == BajaEquipoViewModel.Otro).Elegida = true;
        dialogo.Observacion = observacion;

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Equal("Describe el motivo", dialogo.EtiquetaObservacion);
        Assert.Equal("Escribe el motivo (al menos 10 caracteres).", dialogo.ErrorObservacion);
        await _servicio.DidNotReceiveWithAnyArgs().CambiarEstadoAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData("Daño irreparable", "", "Daño irreparable")]
    [InlineData("Daño irreparable", "  Tarjeta principal quemada ", "Daño irreparable · Tarjeta principal quemada")]
    [InlineData("Otro", "Robo reportado a la policía", "Robo reportado a la policía")]
    public void Motivo_SeArmaConLaOpcionYLaObservacion(string opcion, string observacion, string esperado)
    {
        var dialogo = new BajaEquipoViewModel("HSV-EQ-0412", "Monitor", esReversion: false);
        dialogo.Opciones.Single(o => o.Texto == opcion).Elegida = true;
        dialogo.Observacion = observacion;

        Assert.True(dialogo.Validar());
        Assert.Equal(esperado, dialogo.Motivo);
    }

    [Fact]
    public void Motivo_DeMasDe300Caracteres_NoSeEnvia()
    {
        var dialogo = new BajaEquipoViewModel("HSV-EQ-0412", "Monitor", esReversion: false);
        dialogo.Opciones[0].Elegida = true;
        dialogo.Observacion = new string('x', 290);

        Assert.False(dialogo.Validar());
        Assert.Equal("El motivo admite hasta 300 caracteres.", dialogo.ErrorObservacion);
    }

    [Fact]
    public void ElegirUnaOpcion_DesmarcaLaAnteriorYQuitaElError()
    {
        var dialogo = new BajaEquipoViewModel("HSV-EQ-0412", "Monitor", esReversion: false);
        dialogo.Validar();

        dialogo.Opciones[0].Elegida = true;
        dialogo.Opciones[1].Elegida = true;

        Assert.Equal([false, true, false, false, false], dialogo.Opciones.Select(o => o.Elegida));
        Assert.Null(dialogo.ErrorMotivo);
    }

    [Fact]
    public async Task CA_HU_014_02_Confirmar_EnviaBajaCierraYMuestraLaHojaDeVidaDeBaja()
    {
        var equipo = Equipo();
        var detalle = await AbrirAsync(equipo);
        detalle.AbrirBajaCommand.Execute(null);
        detalle.Baja!.Opciones[1].Elegida = true;
        detalle.Baja.Observacion = "Tarjeta principal quemada";
        _servicio.CambiarEstadoAsync(equipo.Id, EstadosEquipo.Baja, "Daño irreparable · Tarjeta principal quemada", Arg.Any<CancellationToken>())
            .Returns(equipo with { Estado = EstadosEquipo.Baja, EstadoNombre = "Baja", FechaBaja = FechaBaja });

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Null(detalle.Baja);
        Assert.Equal("Baja", detalle.Estado);
        Assert.Equal("HSV-EQ-0412 dado de baja", detalle.Confirmacion!.Titulo);
        Assert.Contains("historial se conservan", detalle.Confirmacion.Mensaje);
        Assert.Equal("Equipo dado de baja el 06/11/2026", detalle.AvisoBaja!.Titulo);
        Assert.True(detalle.PuedeRevertir);
        Assert.Equal(1, _refrescos);
    }

    [Fact]
    public async Task ErrorDeLaApi_MantieneElDialogoAbiertoConElMensaje()
    {
        var equipo = Equipo();
        var detalle = await AbrirAsync(equipo);
        detalle.AbrirBajaCommand.Execute(null);
        detalle.Baja!.Opciones[0].Elegida = true;
        _servicio.CambiarEstadoAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(
            new ErrorApi("EQ-001", "El equipo ya está en el estado «Baja».", new Dictionary<string, string>()));

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.NotNull(detalle.Baja);
        Assert.Equal("No se pudo dar de baja el equipo", detalle.Baja.Aviso!.Titulo);
        Assert.Equal("EQ-001", detalle.Baja.Aviso.Codigo);
        Assert.False(detalle.Baja.Enviando);
        Assert.Equal("Operativo", detalle.Estado);
        Assert.Equal(0, _refrescos);
    }

    [Fact]
    public async Task SinConexion_AvisaConMsg011()
    {
        var detalle = await AbrirAsync(Equipo());
        detalle.AbrirBajaCommand.Execute(null);
        detalle.Baja!.Opciones[0].Elegida = true;
        _servicio.CambiarEstadoAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(
            new ErrorApi(null, Mensajes.Msg011, new Dictionary<string, string>(), sinConexion: true));

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Equal(("Sin conexión", "MSG-011"), (detalle.Baja.Aviso!.Titulo, detalle.Baja.Aviso.Codigo));
    }

    [Fact]
    public async Task Cancelar_CierraSinEnviar()
    {
        var detalle = await AbrirAsync(Equipo());
        detalle.AbrirBajaCommand.Execute(null);

        detalle.CancelarBajaCommand.Execute(null);

        Assert.Null(detalle.Baja);
        await _servicio.DidNotReceiveWithAnyArgs().CambiarEstadoAsync(default, default!, default!, default);
    }

    // ---------- Revertir la baja (RN-009) ----------

    [Fact]
    public async Task Reversion_PideUnMotivoDeAlMenos10Caracteres()
    {
        var detalle = await AbrirAsync(DeBaja());
        detalle.AbrirReversionCommand.Execute(null);
        var dialogo = detalle.Baja!;
        dialogo.Observacion = "Reparado";

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Equal("Revertir la baja de HSV-EQ-0412", dialogo.Titulo);
        Assert.Equal(("Revertir baja", "Motivo de la reversión"), (dialogo.TextoConfirmar, dialogo.EtiquetaObservacion));
        Assert.Empty(dialogo.Opciones);
        Assert.Equal("Escribe el motivo (al menos 10 caracteres).", dialogo.ErrorObservacion);
    }

    [Fact]
    public async Task Reversion_PideOperativoYVuelveAlInventarioActivo()
    {
        var equipo = DeBaja();
        var detalle = await AbrirAsync(equipo);
        detalle.AbrirReversionCommand.Execute(null);
        detalle.Baja!.Observacion = "Se consiguieron los repuestos";
        _servicio.CambiarEstadoAsync(equipo.Id, EstadosEquipo.Operativo, "Se consiguieron los repuestos", Arg.Any<CancellationToken>())
            .Returns(equipo with { Estado = EstadosEquipo.Operativo, EstadoNombre = "Operativo", FechaBaja = null });

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Null(detalle.Baja);
        Assert.Equal("Operativo", detalle.Estado);
        Assert.Null(detalle.AvisoBaja);
        Assert.True(detalle.PuedeEditar);
        Assert.Equal("Baja de HSV-EQ-0412 revertida", detalle.Confirmacion!.Titulo);
    }

    [Fact]
    public async Task Reversion_ConElPlanLleno_MuestraRn009()
    {
        var detalle = await AbrirAsync(DeBaja());
        detalle.AbrirReversionCommand.Execute(null);
        detalle.Baja!.Observacion = "Se consiguieron los repuestos";
        _servicio.CambiarEstadoAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(
            new ErrorApi("RN-009", "Se alcanzó el límite de 300 equipos activos del plan Consultorio.", new Dictionary<string, string>()));

        await detalle.ConfirmarBajaCommand.ExecuteAsync(null);

        Assert.Equal("No se pudo revertir la baja", detalle.Baja.Aviso!.Titulo);
        Assert.Equal("RN-009", detalle.Baja.Aviso.Codigo);
        Assert.True(detalle.EstaDeBaja);
    }

    [Fact]
    public void MotivoRechazadoPorLaApi_SeMarcaEnElCampo()
    {
        var dialogo = new BajaEquipoViewModel("HSV-EQ-0412", "Monitor", esReversion: true);

        dialogo.MostrarError(new ErrorApi(null, "El motivo es obligatorio.", new Dictionary<string, string> { ["motivo"] = "El motivo es obligatorio." }));

        Assert.Equal("El motivo es obligatorio.", dialogo.ErrorObservacion);
    }
}
