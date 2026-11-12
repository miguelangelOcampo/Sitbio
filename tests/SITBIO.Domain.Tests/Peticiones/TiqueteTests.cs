using Sitbio.Domain.Peticiones;

namespace Sitbio.Domain.Tests.Peticiones;

/// <summary>Tiquete (HU-003, HU-018): estado inicial, código TQT-######, criticidad y primer registro del historial.</summary>
public class TiqueteTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private static Tiquete Nuevo(string criticidad = Criticidades.Alta, string descripcion = "No enciende al conectarlo.") =>
        new(Tenant, Guid.NewGuid(), Guid.NewGuid(), "No enciende", descripcion, criticidad, Ahora);

    [Fact]
    public void Nuevo_NaceRecibidoSinCodigoYConLaPrioridadDeSuCriticidad()
    {
        var tiquete = Nuevo(Criticidades.Media);

        Assert.Equal((EstadosTiquete.Recibido, string.Empty, Ahora), (tiquete.Estado, tiquete.Codigo, tiquete.FechaReporte));
        Assert.Equal(Criticidades.Media, tiquete.PrioridadSugerida);
        Assert.False(tiquete.EsCritica);
        Assert.True(Nuevo(Criticidades.Critica).EsCritica);
    }

    [Theory]
    [InlineData(1, "TQT-000001")]
    [InlineData(42, "TQT-000042")]
    [InlineData(999_999, "TQT-999999")]
    public void Numerar_UsaSeisDigitos(int consecutivo, string codigo)
    {
        var tiquete = Nuevo();

        tiquete.Numerar(consecutivo);

        Assert.Equal(codigo, tiquete.Codigo);
        Assert.Equal(consecutivo, Tiquete.Consecutivo(codigo));
    }

    [Fact]
    public void Numerar_SoloUnaVezYDentroDelRango()
    {
        var tiquete = Nuevo();
        tiquete.Numerar(7);

        Assert.Throws<InvalidOperationException>(() => tiquete.Numerar(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Nuevo().Numerar(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Nuevo().Numerar(1_000_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TQT-12")]
    [InlineData("OT-000001")]
    [InlineData("TQT-00000A")]
    [InlineData("TQT--00001")]
    public void Consecutivo_SinFormato_EsNulo(string? codigo) => Assert.Null(Tiquete.Consecutivo(codigo));

    [Theory]
    [InlineData("urgente")]
    [InlineData("Critica")]
    [InlineData("")]
    public void CriticidadFueraDelCatalogo_SeRechaza(string criticidad) =>
        Assert.Throws<ArgumentException>(() => Nuevo(criticidad));

    [Fact]
    public void SinEquipoODescripcion_SeRechaza()
    {
        Assert.Throws<ArgumentException>(() => new Tiquete(Tenant, Guid.Empty, Guid.NewGuid(), "No enciende", "Falla", Criticidades.Baja, Ahora));
        Assert.Throws<ArgumentException>(() => Nuevo(descripcion: "  "));
        Assert.Throws<ArgumentException>(() => Nuevo(descripcion: new string('x', Tiquete.LongitudMaximaDescripcion + 1)));
    }

    [Fact]
    public void Recepcion_EsElPrimerRegistroDelHistorial()
    {
        var tiquete = Nuevo();

        var registro = tiquete.Recepcion();

        Assert.Equal((tiquete.Id, tiquete.ReportanteId, Tenant), (registro.TiqueteId, registro.UsuarioId, registro.TenantId));
        Assert.Equal(((string?)null, EstadosTiquete.Recibido, Ahora), (registro.EstadoAnterior, registro.NuevoEstado, registro.FechaCambio));
    }
}
