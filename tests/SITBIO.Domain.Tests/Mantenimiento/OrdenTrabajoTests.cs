using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Domain.Tests.Mantenimiento;

/// <summary>Orden de trabajo correctiva (HU-004): nace asignada desde un tiquete recibido, que pasa a «asignado».</summary>
public class OrdenTrabajoTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Reporte = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ahora = Reporte.AddMinutes(20);
    private readonly Guid _tecnico = Guid.NewGuid();
    private readonly Guid _ingeniero = Guid.NewGuid();

    private static Tiquete Recibido(string criticidad = Criticidades.Alta)
    {
        var tiquete = new Tiquete(Tenant, Guid.NewGuid(), Guid.NewGuid(), "No enciende", null, criticidad, Reporte);
        tiquete.Numerar(7);
        return tiquete;
    }

    [Fact]
    public void Correctiva_NaceAsignadaConLaPrioridadSugeridaYElTiquetePasaAAsignado()
    {
        var tiquete = Recibido(Criticidades.Critica);
        var programada = new DateOnly(2026, 11, 14);

        var (orden, cambio) = OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, null, programada, Ahora);

        Assert.Equal((Tenant, tiquete.EquipoId, (Guid?)tiquete.Id), (orden.TenantId, orden.EquipoId, orden.TiqueteId));
        Assert.Equal((_tecnico, (Guid?)_ingeniero), (orden.TecnicoId, orden.AsignadoPorId));
        Assert.Equal((TiposMantenimiento.Correctivo, Criticidades.Critica, EstadosOrden.Asignada), (orden.TipoMantenimiento, orden.Prioridad, orden.Estado));
        Assert.Equal(((DateOnly?)programada, (DateTimeOffset?)Ahora, Ahora), (orden.FechaProgramada, orden.FechaAsignacion, orden.FechaCreacion));

        Assert.Equal(EstadosTiquete.Asignado, tiquete.Estado);
        Assert.Equal((tiquete.Id, _ingeniero, Ahora), (cambio.TiqueteId, cambio.UsuarioId, cambio.FechaCambio));
        Assert.Equal((EstadosTiquete.Recibido, EstadosTiquete.Asignado), (cambio.EstadoAnterior, cambio.NuevoEstado));
        Assert.Equal(Tenant, cambio.TenantId);
    }

    // ---------- HU-005: ejecución ----------

    [Fact]
    public void Iniciar_LaPrimeraVezFijaLaFechaDeInicio_YReanudarNoLaCambia()
    {
        var (orden, _) = OrdenTrabajo.Correctiva(Recibido(), _tecnico, _ingeniero, null, null, Ahora);

        Assert.True(orden.Iniciar(Ahora.AddHours(1)));
        Assert.Equal((EstadosOrden.EnEjecucion, (DateTimeOffset?)Ahora.AddHours(1), true), (orden.Estado, orden.FechaInicio, orden.AdmiteIntervencion));

        orden.Pausar();
        Assert.Equal((EstadosOrden.Pausada, true), (orden.Estado, orden.AdmiteIntervencion));

        Assert.False(orden.Iniciar(Ahora.AddHours(3)));
        Assert.Equal((EstadosOrden.EnEjecucion, (DateTimeOffset?)Ahora.AddHours(1)), (orden.Estado, orden.FechaInicio));
    }

    [Fact]
    public void Transiciones_FueraDeOrden_SeRechazan()
    {
        var (orden, _) = OrdenTrabajo.Correctiva(Recibido(), _tecnico, _ingeniero, null, null, Ahora);

        Assert.False(orden.AdmiteIntervencion);
        Assert.Throws<InvalidOperationException>(orden.Pausar);
        orden.Iniciar(Ahora);
        Assert.Throws<InvalidOperationException>(() => orden.Iniciar(Ahora));
        Assert.Equal(EstadosOrden.EnEjecucion, orden.Estado);
    }

    [Theory]
    [InlineData("65f1a2b3c4d5e6f7a8b9c0d1", true)]
    [InlineData("65F1A2B3C4D5E6F7A8B9C0D1", true)]
    [InlineData("65f1a2b3c4d5e6f7a8b9c0d", false)]
    [InlineData("65f1a2b3c4d5e6f7a8b9c0dz", false)]
    [InlineData("", false)]
    public void EnlazarIntervencion_SoloAceptaUnObjectId(string objectId, bool valido)
    {
        var (orden, _) = OrdenTrabajo.Correctiva(Recibido(), _tecnico, _ingeniero, null, null, Ahora);

        if (valido)
        {
            orden.EnlazarIntervencion(objectId);
            Assert.Equal(objectId, orden.IntervencionId);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => orden.EnlazarIntervencion(objectId));
            Assert.Null(orden.IntervencionId);
        }
    }

    [Fact]
    public void Tiquete_IniciarEjecucion_SoloDesdeAsignado()
    {
        var tiquete = Recibido();
        Assert.Throws<InvalidOperationException>(() => tiquete.IniciarEjecucion(_tecnico, "Inició", Ahora));
        OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, null, null, Ahora);

        var cambio = tiquete.IniciarEjecucion(_tecnico, "Inició", Ahora.AddHours(1));

        Assert.Equal(EstadosTiquete.EnEjecucion, tiquete.Estado);
        Assert.Equal((EstadosTiquete.Asignado, EstadosTiquete.EnEjecucion), (cambio.EstadoAnterior, cambio.NuevoEstado));
        Assert.Equal((_tecnico, Ahora.AddHours(1), Tenant, tiquete.Id), (cambio.UsuarioId, cambio.FechaCambio, cambio.TenantId, cambio.TiqueteId));
        Assert.Throws<InvalidOperationException>(() => tiquete.IniciarEjecucion(_tecnico, "Inició", Ahora));
    }

    [Fact]
    public void Correctiva_ConOtraPrioridad_LaRespeta()
    {
        var (orden, _) = OrdenTrabajo.Correctiva(Recibido(Criticidades.Baja), _tecnico, _ingeniero, Criticidades.Alta, null, Ahora);

        Assert.Equal(Criticidades.Alta, orden.Prioridad);
        Assert.Null(orden.FechaProgramada);
    }

    [Fact]
    public void Correctiva_ConPrioridadInvalidaOSinTecnico_NoCambiaElTiquete()
    {
        var tiquete = Recibido();

        Assert.Throws<ArgumentException>(() => OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, "urgente", null, Ahora));
        Assert.Throws<ArgumentException>(() => OrdenTrabajo.Correctiva(tiquete, Guid.Empty, _ingeniero, null, null, Ahora));

        Assert.Equal(EstadosTiquete.Recibido, tiquete.Estado);
    }

    [Fact]
    public void Asignar_UnTiqueteYaAsignado_NoSePermite()
    {
        var tiquete = Recibido();
        OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, null, null, Ahora);

        var error = Assert.Throws<InvalidOperationException>(() => OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, null, null, Ahora));

        Assert.Contains("TQT-000007", error.Message);
        Assert.Equal(EstadosTiquete.Asignado, tiquete.Estado);
    }

    // ---------- HU-021: cierre y aprobación ----------

    private OrdenTrabajo EnEjecucion(bool conIntervencion = true)
    {
        var (orden, _) = OrdenTrabajo.Correctiva(Recibido(), _tecnico, _ingeniero, null, null, Ahora);
        orden.Iniciar(Ahora.AddMinutes(5));
        if (conIntervencion)
        {
            orden.EnlazarIntervencion("65f1a2b3c4d5e6f7a8b9c0d1");
        }

        return orden;
    }

    [Fact]
    public void Cerrar_ConLaIntervencion_QuedaCerradaConSuFecha_DesdeEjecucionOPausada()
    {
        var orden = EnEjecucion();
        orden.Cerrar(Ahora.AddHours(1));
        Assert.Equal((EstadosOrden.Cerrada, (DateTimeOffset?)Ahora.AddHours(1)), (orden.Estado, orden.FechaCierre));
        Assert.False(orden.AdmiteIntervencion);

        var pausada = EnEjecucion();
        pausada.Pausar();
        pausada.Cerrar(Ahora.AddHours(2));
        Assert.Equal(EstadosOrden.Cerrada, pausada.Estado);
    }

    [Fact]
    public void Cerrar_SinIntervencionOSinIniciar_NoCambiaLaOrden()
    {
        var sinIntervencion = EnEjecucion(conIntervencion: false);
        var error = Assert.Throws<InvalidOperationException>(() => sinIntervencion.Cerrar(Ahora));
        Assert.Contains("Registra la intervención", error.Message);
        Assert.Equal((EstadosOrden.EnEjecucion, (DateTimeOffset?)null), (sinIntervencion.Estado, sinIntervencion.FechaCierre));

        var (asignada, _) = OrdenTrabajo.Correctiva(Recibido(), _tecnico, _ingeniero, null, null, Ahora);
        Assert.Throws<InvalidOperationException>(() => asignada.Cerrar(Ahora));
        Assert.Equal(EstadosOrden.Asignada, asignada.Estado);
    }

    [Fact]
    public void Aprobar_SoloUnaOrdenCerrada_GuardaFirmaFechaYQuienAprueba()
    {
        var orden = EnEjecucion();
        Assert.Throws<InvalidOperationException>(() => orden.Aprobar(_ingeniero, "Laura Gómez", Ahora));

        orden.Cerrar(Ahora.AddHours(1));
        orden.Aprobar(_ingeniero, "  Laura Gómez  ", Ahora.AddHours(2));

        Assert.Equal(EstadosOrden.Aprobada, orden.Estado);
        Assert.Equal(((Guid?)_ingeniero, "Laura Gómez", (DateTimeOffset?)Ahora.AddHours(2)), (orden.AprobadoPorId, orden.FirmaAprobacion, orden.FechaAprobacion));
        Assert.Throws<InvalidOperationException>(() => orden.Aprobar(_ingeniero, "Otra firma", Ahora.AddHours(3)));
        Assert.Equal("Laura Gómez", orden.FirmaAprobacion);
    }

    [Fact]
    public void Aprobar_SinFirmaOFirmaLarga_NoCambiaLaOrden()
    {
        var orden = EnEjecucion();
        orden.Cerrar(Ahora.AddHours(1));

        Assert.Throws<ArgumentException>(() => orden.Aprobar(_ingeniero, "   ", Ahora));
        Assert.Throws<ArgumentException>(() => orden.Aprobar(_ingeniero, new string('x', OrdenTrabajo.LongitudMaximaFirma + 1), Ahora));
        Assert.Throws<ArgumentException>(() => orden.Aprobar(Guid.Empty, "Laura Gómez", Ahora));

        Assert.Equal((EstadosOrden.Cerrada, (Guid?)null, (string?)null), (orden.Estado, orden.AprobadoPorId, orden.FirmaAprobacion));
    }

    [Fact]
    public void Tiquete_Cerrar_DesdeAsignadoOEnEjecucion_GuardaLaFechaDeCierre()
    {
        var tiquete = Recibido();
        Assert.Throws<InvalidOperationException>(() => tiquete.Cerrar(_ingeniero, "Aprobada", Ahora));
        OrdenTrabajo.Correctiva(tiquete, _tecnico, _ingeniero, null, null, Ahora);
        tiquete.IniciarEjecucion(_tecnico, "Inició", Ahora.AddMinutes(5));

        var cambio = tiquete.Cerrar(_ingeniero, "Orden aprobada", Ahora.AddHours(2));

        Assert.Equal((EstadosTiquete.Cerrado, (DateTimeOffset?)Ahora.AddHours(2)), (tiquete.Estado, tiquete.FechaCierre));
        Assert.Equal((EstadosTiquete.EnEjecucion, EstadosTiquete.Cerrado, _ingeniero), (cambio.EstadoAnterior, cambio.NuevoEstado, cambio.UsuarioId));
        Assert.Throws<InvalidOperationException>(() => tiquete.Cerrar(_ingeniero, "Otra vez", Ahora.AddHours(3)));
    }
}
