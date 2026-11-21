using Sitbio.Application.Instituciones;

namespace Sitbio.Application.Tests.Instituciones;

/// <summary>J-06: contenido del correo al proveedor y cuándo hay configuración para enviarlo (RF-029).</summary>
public class AvisoSolicitudesTests
{
    private static AvisoSolicitud Aviso(string? plan = "Hospital", string? telefono = "6041234567", string? mensaje = "Tenemos 400 equipos.") => new(
        "SOL-000012", "cotizacion", plan, "Clínica San Rafael", "900.123.456-7", "Ana Gómez", "ana@sanrafael.co", telefono, mensaje,
        new DateTimeOffset(2026, 11, 20, 19, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Plantilla_VaAlDestinoConLosDatosDeLaSolicitud_YResponderAlContacto()
    {
        var correo = PlantillaAvisoSolicitud.Crear(Aviso(), "proveedor@sitbio.co");

        Assert.Equal("proveedor@sitbio.co", correo.Para);
        Assert.Equal("ana@sanrafael.co", correo.ResponderA);
        Assert.Equal("[SITBIO] SOL-000012 · Cotización · Hospital · Clínica San Rafael", correo.Asunto);
        foreach (var texto in new[]
                 {
                     "SOL-000012", "Tipo: Cotización", "Plan de interés: Hospital", "Institución: Clínica San Rafael", "NIT: 900.123.456-7",
                     "Persona de contacto: Ana Gómez", "Correo: ana@sanrafael.co", "Teléfono: 6041234567", "Tenemos 400 equipos.",
                     "Recibida: 20/11/2026 14:30 (hora de Colombia)",
                 })
        {
            Assert.Contains(texto, correo.Texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Plantilla_SinPlanTelefonoNiMensaje_LoDiceEnTexto()
    {
        var correo = PlantillaAvisoSolicitud.Crear(Aviso(plan: null, telefono: null, mensaje: null) with { Tipo = "demostracion" }, "p@sitbio.co");

        Assert.Equal("[SITBIO] SOL-000012 · Demostración · Por definir · Clínica San Rafael", correo.Asunto);
        Assert.Contains("Teléfono: No lo indicó", correo.Texto, StringComparison.Ordinal);
        Assert.Contains("Sin mensaje.", correo.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("demostracion", "Demostración")]
    [InlineData("cotizacion", "Cotización")]
    [InlineData("cambio_plan", "Cambio de plan")]
    public void NombreTipo_ComoLoLeeElProveedor(string tipo, string nombre) => Assert.Equal(nombre, PlantillaAvisoSolicitud.NombreTipo(tipo));

    [Fact]
    public void Opciones_SinServidorRemitenteODestino_NoEstanConfiguradas()
    {
        Assert.False(new OpcionesCorreo { Servidor = "smtp.gmail.com", Destino = "p@sitbio.co" }.EstaConfigurado);
        Assert.False(new OpcionesCorreo { Servidor = "smtp.gmail.com", Usuario = "u@gmail.com" }.EstaConfigurado);
        Assert.False(new OpcionesCorreo { Usuario = "u@gmail.com", Destino = "p@sitbio.co" }.EstaConfigurado);

        var gmail = new OpcionesCorreo { Servidor = "smtp.gmail.com", Usuario = "u@gmail.com", Destino = "p@sitbio.co" };
        Assert.True(gmail.EstaConfigurado);
        Assert.Equal("u@gmail.com", gmail.RemitenteEfectivo);
        Assert.Equal((587, SeguridadSmtp.StartTls), (gmail.Puerto, gmail.Seguridad));
        Assert.Equal("avisos@sitbio.co", new OpcionesCorreo { Usuario = "u@gmail.com", Remitente = "avisos@sitbio.co" }.RemitenteEfectivo);
    }
}
