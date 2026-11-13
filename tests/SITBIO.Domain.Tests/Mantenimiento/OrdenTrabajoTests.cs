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
}
