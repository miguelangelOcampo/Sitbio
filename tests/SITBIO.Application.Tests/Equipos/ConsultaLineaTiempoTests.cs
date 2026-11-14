using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;

namespace Sitbio.Application.Tests.Equipos;

/// <summary>
/// Línea de tiempo del equipo (HU-023, CA-HU-023-01): registro, estados, tiquetes, órdenes, intervenciones y
/// calibraciones en orden cronológico; si MongoDB no responde, se devuelve lo demás y se avisa.
/// </summary>
public class ConsultaLineaTiempoTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Inicio = new(2026, 1, 10, 15, 0, 0, TimeSpan.Zero);

    private readonly IRepositorioLineaTiempo _repositorio = Substitute.For<IRepositorioLineaTiempo>();
    private readonly IRepositorioIntervenciones _intervenciones = Substitute.For<IRepositorioIntervenciones>();
    private readonly Guid _usuario = Guid.NewGuid();

    private (HistoriaEquipo Historia, OrdenTrabajo Orden) Historia()
    {
        var equipo = new EquipoBiomedico(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SN-1", "MON-07", "Mindray", "T5", Inicio);
        var baja = equipo.DarDeBaja("Obsoleto", _usuario, Inicio.AddDays(200));
        var tiquete = new Tiquete(Tenant, equipo.Id, _usuario, "No enciende", "No prende.", Criticidades.Alta, Inicio.AddDays(30));
        var plan = new PlanPreventivo(Tenant, equipo.Id, "Preventivo", 90, DateOnly.FromDateTime(Inicio.AddDays(60).UtcDateTime), Inicio);
        var orden = OrdenTrabajo.Preventiva(plan, _usuario, Inicio.AddDays(60));
        var calibracion = new RegistroCalibracion(Tenant, equipo.Id, _usuario, new DateOnly(2026, 3, 1), "Analizador", RegistroCalibracion.Conforme, new DateOnly(2027, 3, 1));
        var historia = new HistoriaEquipo(
            equipo.Id,
            "MON-07",
            Inicio,
            [new EstadoLeido(baja, "Operativo", "Baja", "Laura Gómez")],
            [tiquete],
            [new OrdenDelEquipo(orden, "Ana Mora", null, null)],
            [new CalibracionDelEquipo(calibracion, "Ana Mora")]);
        _repositorio.HistoriaAsync(equipo.Id, Arg.Any<CancellationToken>()).Returns(historia);
        return (historia, orden);
    }

    private static Intervencion Intervencion(Guid equipo, Guid orden, DateTimeOffset fecha) =>
        new(Guid.NewGuid(), orden, equipo, Guid.NewGuid(), TiposMantenimiento.Preventivo, "Filtros sucios", "Limpieza y prueba", [], 10000, 10000, 60,
            EstadosSalidaEquipo.Operativo, new Dictionary<string, object>(), null, fecha, true);

    [Fact]
    public async Task Consultar_UneTodoEnOrdenCronologico()
    {
        var (historia, orden) = Historia();
        _intervenciones.DelEquipoAsync(historia.EquipoId, Arg.Any<CancellationToken>()).Returns([Intervencion(historia.EquipoId, orden.Id, Inicio.AddDays(61))]);

        var linea = await new ConsultaLineaTiempo(_repositorio, _intervenciones).ConsultarAsync(historia.EquipoId, CancellationToken.None);

        Assert.True(linea.IntervencionesCompletas);
        Assert.Equal(
            [TiposEvento.Registro, TiposEvento.Tiquete, TiposEvento.Calibracion, TiposEvento.Orden, TiposEvento.Intervencion, TiposEvento.Estado],
            linea.Eventos.Select(e => e.Tipo));
        Assert.Equal(linea.Eventos.OrderBy(e => e.Fecha).Select(e => e.Fecha), linea.Eventos.Select(e => e.Fecha));
        Assert.Equal(("Calibración conforme", "Patrón Analizador · vence el 01/03/2027"), (linea.Eventos[2].Titulo, linea.Eventos[2].Detalle));
        Assert.Equal(("Orden preventiva asignada", "Generada por el plan preventivo", "Ana Mora"), (linea.Eventos[3].Titulo, linea.Eventos[3].Detalle, linea.Eventos[3].Responsable));
        Assert.Equal(("Intervención registrada · sale «Operativo»", "Ana Mora"), (linea.Eventos[4].Titulo, linea.Eventos[4].Responsable));
        Assert.Equal(("Estado: Operativo → Baja", "Obsoleto", "Laura Gómez"), (linea.Eventos[5].Titulo, linea.Eventos[5].Detalle, linea.Eventos[5].Responsable));
    }

    [Fact]
    public async Task SinMongo_DevuelveLoDePostgres_YAvisa()
    {
        var (historia, _) = Historia();
        _intervenciones.DelEquipoAsync(historia.EquipoId, Arg.Any<CancellationToken>()).ThrowsAsync(new ErrorServicioNoDisponible(new TimeoutException()));

        var linea = await new ConsultaLineaTiempo(_repositorio, _intervenciones).ConsultarAsync(historia.EquipoId, CancellationToken.None);

        Assert.False(linea.IntervencionesCompletas);
        Assert.Equal(5, linea.Eventos.Count);
    }

    [Fact]
    public async Task EquipoAjenoOInexistente_Responde404() =>
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => new ConsultaLineaTiempo(_repositorio, _intervenciones).ConsultarAsync(Guid.NewGuid(), CancellationToken.None));
}
