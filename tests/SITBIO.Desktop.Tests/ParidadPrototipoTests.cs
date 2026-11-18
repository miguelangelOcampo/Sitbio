using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests;

/// <summary>D-07: datos de presentación que igualan el prototipo v0.24.</summary>
public class ParidadPrototipoTests
{
    [Theory]
    [InlineData("Mantenimiento", "Icono.Calendario")]
    [InlineData("Calibración", "Icono.Regla")]
    [InlineData("Orden de trabajo", "Icono.Portapapeles")]
    [InlineData("Solicitud", "Icono.Bandeja")]
    [InlineData("Equipo", "Icono.Equipo")]
    [InlineData("Otro", "Icono.Alerta")]
    public void La_tarjeta_de_atencion_usa_el_icono_de_su_tipo(string tipo, string icono)
    {
        var tarjeta = new TarjetaAtencion("k", tipo, "Alta", TonoTarjeta.Alto, "Título", "Descripción", "Hoy", "UI-005", null);

        Assert.Equal(icono, tarjeta.Icono);
    }

    [Fact]
    public void Cada_criticidad_tiene_descripcion_corta_icono_y_color()
    {
        var opciones = ReportarFallaViewModel.OpcionesCriticidad;

        Assert.Equal(
            ["No se puede usar y afecta la atención.", "Funciona con fallas importantes.", "Falla parcial; hay alternativa.", "Detalle menor o estético."],
            opciones.Select(o => o.Breve));
        Assert.Equal(["Icono.AlertaOctagono", "Icono.AlertaTriangulo", "Icono.AlertaTriangulo", "Icono.Circulo"], opciones.Select(o => o.Icono));
        Assert.Equal(["Brush.Danger", "Brush.High", "Brush.Warning", "Brush.Text3"], opciones.Select(o => o.ColorIcono));
    }
}
