using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

/// <summary>Estado operativo, baja lógica y reversión del equipo (RF-014, HU-014).</summary>
public class EstadoEquipoTests
{
    private static readonly DateTimeOffset Creacion = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Momento = new(2026, 11, 10, 15, 30, 0, TimeSpan.Zero);
    private static readonly Guid Ingeniero = Guid.NewGuid();

    private static EquipoBiomedico NuevoEquipo() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SN-001", "PL-001", "Mindray", "BeneView T5", Creacion);

    [Fact]
    public void Alta_PrimerRegistroSinEstadoAnteriorNiMotivo()
    {
        var equipo = NuevoEquipo();

        var alta = HistorialEstadoEquipo.Alta(equipo, Ingeniero);

        Assert.Equal((equipo.TenantId, equipo.Id), (alta.TenantId, alta.EquipoId));
        Assert.Null(alta.EstadoAnteriorId);
        Assert.Equal(EstadosEquipo.Operativo, alta.NuevoEstadoId);
        Assert.Null(alta.Motivo);
        Assert.Equal(Creacion, alta.FechaCambio);
        Assert.Equal(Ingeniero, alta.UsuarioId);
    }

    [Fact]
    public void CA_HU_014_01_CambiarEstado_RegistraAnteriorNuevoMotivoFechaYUsuario()
    {
        var equipo = NuevoEquipo();

        var cambio = equipo.CambiarEstado(EstadosEquipo.FueraDeServicio, "  Falla en la fuente de poder. ", Ingeniero, Momento);

        Assert.Equal(EstadosEquipo.FueraDeServicio, equipo.EstadoEquipoId);
        Assert.False(equipo.DadoDeBaja);
        Assert.Equal((equipo.TenantId, equipo.Id), (cambio.TenantId, cambio.EquipoId));
        Assert.Equal(EstadosEquipo.Operativo, cambio.EstadoAnteriorId);
        Assert.Equal(EstadosEquipo.FueraDeServicio, cambio.NuevoEstadoId);
        Assert.Equal("Falla en la fuente de poder.", cambio.Motivo);
        Assert.Equal((Momento, Ingeniero), (cambio.FechaCambio, cambio.UsuarioId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void CambiarEstado_SinMotivo_NoCambiaNada(string? motivo)
    {
        var equipo = NuevoEquipo();

        Assert.Throws<ArgumentException>(() => equipo.CambiarEstado(EstadosEquipo.EnMantenimiento, motivo, Ingeniero, Momento));
        Assert.Equal(EstadosEquipo.Operativo, equipo.EstadoEquipoId);
    }

    [Fact]
    public void CambiarEstado_MotivoMasLargoQueLaColumna_LanzaExcepcion()
    {
        var equipo = NuevoEquipo();
        var motivo = new string('m', HistorialEstadoEquipo.LongitudMaximaMotivo + 1);

        Assert.Throws<ArgumentException>(() => equipo.CambiarEstado(EstadosEquipo.EnMantenimiento, motivo, Ingeniero, Momento));
    }

    [Fact]
    public void CambiarEstado_SinUsuario_NoCambiaNada()
    {
        var equipo = NuevoEquipo();

        Assert.Throws<ArgumentException>(() => equipo.CambiarEstado(EstadosEquipo.EnMantenimiento, "Revisión", Guid.Empty, Momento));
        Assert.Equal(EstadosEquipo.Operativo, equipo.EstadoEquipoId);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)5)]
    public void CambiarEstado_EstadoInexistente_LanzaExcepcion(short estado)
    {
        Assert.Throws<ArgumentException>(() => NuevoEquipo().CambiarEstado(estado, "Motivo", Ingeniero, Momento));
    }

    [Fact]
    public void CambiarEstado_AlMismoEstado_EsUnConflicto()
    {
        Assert.Throws<InvalidOperationException>(() => NuevoEquipo().CambiarEstado(EstadosEquipo.Operativo, "Motivo", Ingeniero, Momento));
    }

    [Fact]
    public void CA_HU_014_02_DarDeBaja_DejaDeEstarActivoYConservaLaHojaDeVida()
    {
        var equipo = NuevoEquipo();

        var baja = equipo.DarDeBaja("Obsoleto, sin repuestos del fabricante.", Ingeniero, Momento);

        Assert.True(equipo.DadoDeBaja);
        Assert.Equal(EstadosEquipo.Baja, equipo.EstadoEquipoId);
        Assert.Equal(Momento, equipo.FechaBaja);
        Assert.Equal(EstadosEquipo.Baja, baja.NuevoEstadoId);
        // La baja es lógica: la hoja de vida no se toca.
        Assert.Equal(("PL-001", "SN-001", "Mindray"), (equipo.PlacaInventario, equipo.NumeroSerie, equipo.Marca));
    }

    [Fact]
    public void CambiarEstado_AEstadoBaja_TambienFijaLaFechaDeBaja()
    {
        var equipo = NuevoEquipo();

        equipo.CambiarEstado(EstadosEquipo.Baja, "Obsoleto", Ingeniero, Momento);

        Assert.Equal(Momento, equipo.FechaBaja);
    }

    [Fact]
    public void DadoDeBaja_NoAdmiteOtroCambioNiOtraBaja()
    {
        var equipo = NuevoEquipo();
        equipo.DarDeBaja("Obsoleto", Ingeniero, Momento);

        Assert.Throws<InvalidOperationException>(() => equipo.CambiarEstado(EstadosEquipo.Operativo, "Volvió", Ingeniero, Momento));
        Assert.Throws<InvalidOperationException>(() => equipo.DarDeBaja("Otra vez", Ingeniero, Momento));
        Assert.Equal(EstadosEquipo.Baja, equipo.EstadoEquipoId);
    }

    [Fact]
    public void RevertirBaja_VuelveAOperativoYQuitaLaFechaDeBaja()
    {
        var equipo = NuevoEquipo();
        equipo.CambiarEstado(EstadosEquipo.FueraDeServicio, "Falla", Ingeniero, Momento);
        equipo.DarDeBaja("Irreparable", Ingeniero, Momento.AddDays(1));

        var reversion = equipo.RevertirBaja("Se consiguió el repuesto.", Ingeniero, Momento.AddDays(2));

        Assert.False(equipo.DadoDeBaja);
        Assert.Null(equipo.FechaBaja);
        Assert.Equal(EstadosEquipo.Operativo, equipo.EstadoEquipoId);
        Assert.Equal((EstadosEquipo.Baja, EstadosEquipo.Operativo), (reversion.EstadoAnteriorId!.Value, reversion.NuevoEstadoId));
        Assert.Equal("Se consiguió el repuesto.", reversion.Motivo);
    }

    [Fact]
    public void RevertirBaja_SinMotivo_SigueDeBaja()
    {
        var equipo = NuevoEquipo();
        equipo.DarDeBaja("Obsoleto", Ingeniero, Momento);

        Assert.Throws<ArgumentException>(() => equipo.RevertirBaja(" ", Ingeniero, Momento));
        Assert.True(equipo.DadoDeBaja);
    }

    [Fact]
    public void RevertirBaja_DeUnEquipoActivo_EsUnConflicto()
    {
        Assert.Throws<InvalidOperationException>(() => NuevoEquipo().RevertirBaja("Motivo", Ingeniero, Momento));
    }
}
