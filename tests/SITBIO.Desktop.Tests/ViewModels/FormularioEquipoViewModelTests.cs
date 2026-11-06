using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;
using static Sitbio.Desktop.Tests.ViewModels.EquiposPrueba;

namespace Sitbio.Desktop.Tests.ViewModels;

public class FormularioEquipoViewModelTests
{
    private static readonly ElementoCatalogo Tensiometro = new(Guid.NewGuid(), "Tensiómetro", true, "I", true, 12);
    private static readonly ElementoCatalogo Estetoscopio = new(Guid.NewGuid(), "Estetoscopio", true, "I", false, null);
    private static readonly ElementoCatalogo TipoInactivo = new(Guid.NewGuid(), "Equipo retirado", false, "IIa", true, 6);
    private static readonly ElementoCatalogo Urgencias = new(Guid.NewGuid(), "Urgencias", true);
    private static readonly ElementoCatalogo Proveedor = new(Guid.NewGuid(), "Biomédica SAS", true);

    private static FormularioEquipoViewModel Nuevo() =>
        new([Tensiometro, Estetoscopio, TipoInactivo], [Urgencias], [Proveedor], Hoy);

    /// <summary>Formulario nuevo con todo lo obligatorio diligenciado.</summary>
    private static FormularioEquipoViewModel Completo()
    {
        var formulario = Nuevo();
        formulario.Placa = " CSR-EQ-0100 ";
        formulario.Serie = "SN-100";
        formulario.Tipo = Tensiometro;
        formulario.Marca = "Welch Allyn";
        formulario.Modelo = "DS66";
        formulario.RegistroInvima = "INVIMA 2021DM-0012345";
        formulario.VenceRegistro = "31/12/2028";
        formulario.Ubicacion = Urgencias;
        formulario.UltimoPreventivo = "01/10/2026";
        return formulario;
    }

    [Fact]
    public void Nuevo_SoloOfreceTiposActivosYProveedorOpcional()
    {
        var formulario = Nuevo();

        Assert.Equal(["Tensiómetro", "Estetoscopio"], formulario.Tipos.Select(t => t.Nombre));
        Assert.Same(FormularioEquipoViewModel.SinProveedor, formulario.Proveedor);
        Assert.Equal("Registrar equipo biomédico", formulario.Titulo);
        Assert.Equal("Registrar equipo", formulario.TextoGuardar);
    }

    [Fact]
    public void AlElegirElTipo_ProponeClaseYCalibracion()
    {
        var formulario = Nuevo();

        formulario.Tipo = Tensiometro;
        Assert.Equal("I", formulario.Clase!.Valor);
        Assert.True(formulario.RequiereCalibracion);
        Assert.Equal("12", formulario.MesesCalibracion);
        Assert.False(formulario.Difiere);

        formulario.Tipo = Estetoscopio;
        Assert.False(formulario.RequiereCalibracion);
        Assert.Equal(string.Empty, formulario.MesesCalibracion);
    }

    [Fact]
    public void SiElEquipoDifiereDelTipo_Avisa()
    {
        var formulario = Nuevo();
        formulario.Tipo = Tensiometro;

        formulario.MesesCalibracion = "6";
        Assert.True(formulario.Difiere);

        formulario.MesesCalibracion = "12";
        formulario.Clase = FormularioEquipoViewModel.OpcionesClase.Single(o => o.Valor == "IIa");
        Assert.True(formulario.Difiere);
    }

    [Fact]
    public void ProximoPreventivo_SeCalculaComoUltimoMasPeriodicidad()
    {
        var formulario = Nuevo();

        formulario.UltimoPreventivo = "01/10/2026";
        Assert.Equal("30/12/2026", formulario.ProximoPreventivo);

        formulario.Periodicidad = FormularioEquipoViewModel.OpcionesPeriodicidad.Single(o => o.Texto == "Semestral");
        Assert.Equal("30/03/2027", formulario.ProximoPreventivo);
        Assert.False(formulario.PreventivoAtrasado);
    }

    [Fact]
    public void ProximoAjustadoAMano_NoSeRecalcula()
    {
        var formulario = Nuevo();
        formulario.UltimoPreventivo = "01/10/2026";

        formulario.ProximoPreventivo = "15/12/2026";
        formulario.Periodicidad = FormularioEquipoViewModel.OpcionesPeriodicidad.Single(o => o.Texto == "Anual");

        Assert.Equal("15/12/2026", formulario.ProximoPreventivo);
        Assert.Equal("Ajustado a mano.", formulario.AyudaProximo);
    }

    [Fact]
    public void SiElProximoCalculadoYaPaso_AvisaQueNaceVencido()
    {
        var formulario = Nuevo();

        formulario.UltimoPreventivo = "01/03/2026";

        Assert.Equal("30/05/2026", formulario.ProximoPreventivo);
        Assert.True(formulario.PreventivoAtrasado);
    }

    [Fact]
    public void NoSeConoceElUltimo_PideElProximoYNoAdmiteFechasPasadas()
    {
        var formulario = Completo();

        formulario.NoSeConoce = true;
        Assert.Equal(string.Empty, formulario.UltimoPreventivo);
        Assert.Equal(string.Empty, formulario.ProximoPreventivo);
        Assert.False(formulario.Validar());
        Assert.Equal("Indica la fecha del próximo preventivo.", formulario.ErrorProximo);

        formulario.ProximoPreventivo = "01/11/2026";
        Assert.False(formulario.Validar());
        Assert.Equal("El próximo preventivo no puede ser una fecha pasada.", formulario.ErrorProximo);

        formulario.ProximoPreventivo = "20/11/2026";
        Assert.True(formulario.Validar());
        Assert.Equal(new DatosPreventivo(90, null, new DateOnly(2026, 11, 20)), formulario.Datos().Preventivo);
    }

    [Fact]
    public void Validar_MarcaLosCamposObligatoriosConMsg008()
    {
        var formulario = Nuevo();

        Assert.False(formulario.Validar());

        Assert.Equal("MSG-008", formulario.Aviso!.Codigo);
        Assert.NotNull(formulario.ErrorPlaca);
        Assert.NotNull(formulario.ErrorTipo);
        Assert.NotNull(formulario.ErrorRegistro);
        Assert.NotNull(formulario.ErrorClase);
        Assert.NotNull(formulario.ErrorUbicacion);
        Assert.NotNull(formulario.ErrorUltimo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("doce")]
    public void CalibracionFueraDeRango_SeRechaza(string meses)
    {
        var formulario = Completo();

        formulario.MesesCalibracion = meses;

        Assert.False(formulario.Validar());
        Assert.Equal("Escribe un número de meses entre 1 y 60.", formulario.ErrorMeses);
    }

    [Fact]
    public void Datos_ArmaElCuerpoParaLaApi()
    {
        var formulario = Completo();
        formulario.Proveedor = Proveedor;
        formulario.FechaAdquisicion = "15/3/2022";

        Assert.True(formulario.Validar());
        var datos = formulario.Datos();

        Assert.Equal("CSR-EQ-0100", datos.PlacaInventario);
        Assert.Equal(Tensiometro.Id, datos.TipoEquipoId);
        Assert.Equal(Proveedor.Id, datos.ProveedorId);
        Assert.Equal(new DateOnly(2022, 3, 15), datos.FechaAdquisicion);
        Assert.Equal("registro_sanitario", datos.TipoAutorizacion);
        Assert.Equal(new DateOnly(2028, 12, 31), datos.VenceRegistro);
        Assert.Equal(("I", true, 12), (datos.ClaseRiesgo, datos.RequiereCalibracion, datos.PeriodicidadCalibracionMeses));
        Assert.Equal(new DatosPreventivo(90, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 30)), datos.Preventivo);
    }

    [Fact]
    public void SinCalibracion_NoEnviaPeriodicidad()
    {
        var formulario = Completo();
        formulario.Tipo = Estetoscopio;

        Assert.True(formulario.Validar());
        Assert.Equal((false, (int?)null), (formulario.Datos().RequiereCalibracion!.Value, formulario.Datos().PeriodicidadCalibracionMeses));
    }

    [Fact]
    public void AutorizacionPorVencer_AvisaSinBloquear()
    {
        var formulario = Completo();

        formulario.VenceRegistro = "01/03/2027";

        Assert.Equal("La autorización vence en 115 días. Verifica su renovación a tiempo.", formulario.AvisoVencimiento);
        Assert.True(formulario.Validar());
    }

    [Fact]
    public void Editar_CargaLosDatosDelEquipoYSuPlan()
    {
        var equipo = Equipo(plan: Plan(new DateOnly(2026, 12, 1), ultima: new DateOnly(2026, 9, 2))) with
        {
            TipoEquipoId = TipoInactivo.Id,
            UbicacionId = Urgencias.Id,
        };

        var formulario = new FormularioEquipoViewModel([Tensiometro, TipoInactivo], [Urgencias], [Proveedor], Hoy, equipo);

        Assert.Equal($"Editar equipo {equipo.PlacaInventario}", formulario.Titulo);
        Assert.Same(TipoInactivo, formulario.Tipo);
        Assert.Equal("IIb", formulario.Clase!.Valor);
        Assert.Equal("01/01/2028", formulario.VenceRegistro);
        Assert.Equal("02/09/2026", formulario.UltimoPreventivo);
        Assert.Equal("01/12/2026", formulario.ProximoPreventivo);
        Assert.False(formulario.NoSeConoce);
        Assert.Same(FormularioEquipoViewModel.SinProveedor, formulario.Proveedor);
        Assert.StartsWith("Se calcula como último", formulario.AyudaProximo);
    }

    [Fact]
    public void Editar_ProximoDistintoDelCalculo_SeConservaComoAjustado()
    {
        var equipo = Equipo(plan: Plan(new DateOnly(2026, 12, 15), ultima: new DateOnly(2026, 9, 2)));

        var formulario = new FormularioEquipoViewModel([Tensiometro], [Urgencias], [Proveedor], Hoy, equipo);
        formulario.Periodicidad = FormularioEquipoViewModel.OpcionesPeriodicidad.Single(o => o.Texto == "Anual");

        Assert.Equal("15/12/2026", formulario.ProximoPreventivo);
        Assert.Equal("Ajustado a mano.", formulario.AyudaProximo);
    }

    [Fact]
    public void ErrorRn001_SeMuestraEnLaPlaca()
    {
        var formulario = Completo();

        formulario.MostrarError(new ErrorApi("RN-001", "Ya existe un equipo con la placa de inventario CSR-EQ-0100 en tu institución.", new Dictionary<string, string>()));

        Assert.Contains("CSR-EQ-0100", formulario.ErrorPlaca);
        Assert.Null(formulario.ErrorSerie);
        Assert.Equal("MSG-008", formulario.Aviso!.Codigo);
    }

    [Fact]
    public void ErrorRn009_EsElLimiteDelPlanMsg019()
    {
        var formulario = Completo();

        formulario.MostrarError(new ErrorApi("RN-009", "Tu institución alcanzó el máximo de equipos activos del plan Consultorio (300 de 300).", new Dictionary<string, string>()));

        Assert.Equal("Límite del plan alcanzado", formulario.Aviso!.Titulo);
        Assert.Equal("MSG-019", formulario.Aviso.Codigo);
    }

    [Fact]
    public void ErroresDeCampoDeLaApi_SeUbicanEnCadaCampo()
    {
        var formulario = Completo();

        formulario.MostrarError(new ErrorApi(null, "x", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ubicacionId"] = "La ubicación está inactiva.",
            ["preventivo.proximaFecha"] = "El próximo preventivo debe ser posterior al último.",
        }));

        Assert.Equal("La ubicación está inactiva.", formulario.ErrorUbicacion);
        Assert.Equal("El próximo preventivo debe ser posterior al último.", formulario.ErrorProximo);
        Assert.Null(formulario.ErrorPlaca);
    }
}
