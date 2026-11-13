using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Mantenimiento;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Mantenimiento;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioPlanes"/> contra la base real (HU-019, HU-020): el plan se lee con su equipo solo
/// dentro de la institución; la generación de órdenes preventivas toma los planes activos que llegaron a
/// su fecha, no repite la orden (ni con consultas simultáneas) y omite los suspendidos y los equipos dados
/// de baja; las alertas leen los planes y la última vigencia de calibración de los equipos que la requieren.
/// </summary>
public class RepositorioPlanesPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _ingenieroA = Guid.NewGuid();
    private readonly Guid _tecnicoA = Guid.NewGuid();
    private readonly Guid _tecnicoB = Guid.NewGuid();
    private Guid _equipoA;
    private Guid _sinCalibracionA;
    private Guid _deBajaA;
    private Guid _equipoB;

    private (SitbioDbContext Contexto, RepositorioPlanes Planes) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioPlanes(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        var equiposA = await Preparar(_tenantA, [(_ingenieroA, CodigosRol.IngenieroBiomedico, true), (_tecnicoA, CodigosRol.TecnicoMantenimiento, true)], 3);
        (_equipoA, _sinCalibracionA, _deBajaA) = (equiposA[0], equiposA[1], equiposA[2]);
        _equipoB = (await Preparar(_tenantB, [(_tecnicoB, CodigosRol.TecnicoMantenimiento, true)], 1))[0];

        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            var sinCalibracion = await contexto.Equipos.SingleAsync(e => e.Id == _sinCalibracionA);
            sinCalibracion.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "INVIMA 2020DM-0001", Hoy.AddYears(3), ClasesRiesgo.I, false, null);
            var deBaja = await contexto.Equipos.SingleAsync(e => e.Id == _deBajaA);
            contexto.HistorialEstados.Add(deBaja.DarDeBaja("Obsoleto", _ingenieroA, DateTimeOffset.UtcNow));
            await contexto.SaveChangesAsync();
        }
    }

    private async Task<List<Guid>> Preparar(Guid tenant, (Guid Id, string Rol, bool Activo)[] usuarios, int equipos)
    {
        var (contexto, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            var ubicacion = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos, dias_anticipacion_alerta) VALUES ({tenant}, {"Prueba planes"}, {nit}, {"Consultorio"}, {300}, {20})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {"UCI"})");
            foreach (var (id, rol, activo) in usuarios)
            {
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.usuario (id_usuario, tenant_id, rol_id, nombres, apellidos, correo, contrasena_hash, estado_activo) SELECT {id}, {tenant}, id_rol, {"Ana"}, {"Mora"}, {$"{id:N}@prueba-planes.co"}, {"hash"}, {activo} FROM sitbio.rol WHERE codigo = {rol}");
            }

            var tipo = new TipoEquipo(tenant, "Bomba de infusión");
            contexto.Add(tipo);
            var ids = new List<Guid>();
            for (var i = 1; i <= equipos; i++)
            {
                var equipo = new EquipoBiomedico(tenant, tipo.Id, ubicacion, $"SN-PL{i}", $"PL-PL{i}", "Baxter", "Sigma", DateTimeOffset.UtcNow);
                contexto.Add(equipo);
                ids.Add(equipo.Id);
            }

            await contexto.SaveChangesAsync();
            return ids;
        }
    }

    public async Task DisposeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "calibracion", "orden_trabajo", "plan_preventivo", "historial_estado_equipo", "equipo_biomedico", "tipo_equipo", "ubicacion", "usuario" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    private async Task<PlanPreventivo> Plan(Guid tenant, Guid equipo, DateOnly proxima, string estado = PlanPreventivo.Activo)
    {
        var (contexto, planes) = Crear(tenant);
        await using (contexto)
        {
            var plan = new PlanPreventivo(tenant, equipo, "Preventivo mensual", 30, proxima, DateTimeOffset.UtcNow);
            plan.CambiarEstado(estado);
            planes.Agregar(plan);
            await planes.GuardarAsync(CancellationToken.None);
            return plan;
        }
    }

    private async Task<IReadOnlyList<OrdenTrabajo>> Generar(Guid tenant)
    {
        var (contexto, planes) = Crear(tenant);
        await using (contexto)
        {
            return await planes.GenerarOrdenesAsync(Hoy, (p, t) => GestionAlertas.Repartir(p, t, DateTimeOffset.UtcNow), CancellationToken.None);
        }
    }

    [FactIntegracion(Variable)]
    public async Task Plan_SeLeeConSuEquipo_SoloDentroDeLaInstitucion_YSeFiltra()
    {
        var plan = await Plan(_tenantA, _equipoA, Hoy.AddDays(30));
        await Plan(_tenantA, _sinCalibracionA, Hoy.AddDays(10), PlanPreventivo.Suspendido);

        var (contextoA, planesA) = Crear(_tenantA);
        await using (contextoA)
        {
            var leido = await planesA.ObtenerAsync(plan.Id, CancellationToken.None);
            Assert.Equal(("PL-PL1", "Bomba de infusión", "UCI", _equipoA), (leido!.PlacaInventario, leido.TipoEquipo, leido.Ubicacion, leido.Plan.EquipoId));

            var (activos, total) = await planesA.ListarAsync(new FiltroPlanes(null, PlanPreventivo.Activo), CancellationToken.None);
            Assert.Equal((1, plan.Id), (total, Assert.Single(activos).Plan.Id));
            var (delEquipo, _) = await planesA.ListarAsync(new FiltroPlanes(_sinCalibracionA), CancellationToken.None);
            Assert.Equal(PlanPreventivo.Suspendido, Assert.Single(delEquipo).Plan.Estado);

            Assert.Equal(20, await planesA.DiasAnticipacionAsync(CancellationToken.None));
            Assert.Equal(new EquipoDelPlan(_deBajaA, "PL-PL3", true), await planesA.EquipoAsync(_deBajaA, CancellationToken.None));
        }

        var (contextoB, planesB) = Crear(_tenantB);
        await using (contextoB)
        {
            Assert.Null(await planesB.ObtenerAsync(plan.Id, CancellationToken.None));
            Assert.Null(await planesB.PlanAsync(plan.Id, CancellationToken.None));
            Assert.Null(await planesB.EquipoAsync(_equipoA, CancellationToken.None));
            Assert.Equal(0, (await planesB.ListarAsync(new FiltroPlanes(), CancellationToken.None)).Total);
        }
    }

    [FactIntegracion(Variable)]
    public async Task GenerarOrdenes_SoloLosActivosQueLlegaron_UnaVez_YConSusAlertas()
    {
        var vencido = await Plan(_tenantA, _equipoA, Hoy.AddDays(-2));
        var hoy = await Plan(_tenantA, _sinCalibracionA, Hoy);
        var futuro = await Plan(_tenantA, _equipoA, Hoy.AddDays(5));
        await Plan(_tenantA, _equipoA, Hoy.AddDays(-1), PlanPreventivo.Suspendido);
        await Plan(_tenantA, _deBajaA, Hoy.AddDays(-1));
        var deOtra = await Plan(_tenantB, _equipoB, Hoy.AddDays(-1));

        var generadas = await Generar(_tenantA);

        Assert.Equal([vencido.Id, hoy.Id], generadas.Select(o => o.PlanId!.Value));
        var (contexto, planes) = Crear(_tenantA);
        await using (contexto)
        {
            var guardadas = await contexto.Ordenes.AsNoTracking().Where(o => o.PlanId != null).OrderBy(o => o.FechaProgramada).ToListAsync();
            Assert.Equal([(DateOnly?)vencido.ProximaFecha, hoy.ProximaFecha], guardadas.Select(o => o.FechaProgramada));
            Assert.All(guardadas, o => Assert.Equal(
                (TiposMantenimiento.Preventivo, EstadosOrden.Asignada, _tecnicoA, (Guid?)null, (Guid?)null),
                (o.TipoMantenimiento, o.Estado, o.TecnicoId, o.AsignadoPorId, o.TiqueteId)));

            // Otra consulta no repite las órdenes.
            Assert.Empty(await Generar(_tenantA));

            var alertas = await planes.PlanesPorVencerAsync(Hoy.AddDays(5), CancellationToken.None);
            Assert.Equal([vencido.Id, hoy.Id, futuro.Id], alertas.OrderBy(a => a.Leido.Plan.ProximaFecha).Select(a => a.Leido.Plan.Id));
            Assert.Equal(guardadas[0].Id, alertas.Single(a => a.Leido.Plan.Id == vencido.Id).OrdenId);
            Assert.Equal(EstadosOrden.Asignada, alertas.Single(a => a.Leido.Plan.Id == vencido.Id).EstadoOrden);
            Assert.Null(alertas.Single(a => a.Leido.Plan.Id == futuro.Id).OrdenId);
            Assert.Equal(2, (await planes.PlanesPorVencerAsync(Hoy, CancellationToken.None)).Count);
        }

        // La otra institución genera la suya con su propio técnico.
        var deB = Assert.Single(await Generar(_tenantB));
        Assert.Equal((deOtra.Id, _tecnicoB), (deB.PlanId!.Value, deB.TecnicoId));
    }

    [FactIntegracion(Variable)]
    public async Task GenerarOrdenes_ConsultasSimultaneas_NoDuplicanLaOrden()
    {
        await Plan(_tenantA, _equipoA, Hoy.AddDays(-3));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Generar(_tenantA))));

        Assert.Equal(1, resultados.Sum(r => r.Count));
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(1, await contexto.Ordenes.CountAsync(o => o.PlanId != null));
        }
    }

    [FactIntegracion(Variable)]
    public async Task GenerarOrdenes_SinTecnicosActivos_NoGeneraNada()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            await contexto.Database.ExecuteSqlAsync($"UPDATE sitbio.usuario SET estado_activo = false WHERE id_usuario = {_tecnicoA}");
        }

        await Plan(_tenantA, _equipoA, Hoy.AddDays(-3));

        Assert.Empty(await Generar(_tenantA));
    }

    [FactIntegracion(Variable)]
    public async Task Calibraciones_LaUltimaVigenciaDeLosEquiposQueLaRequieren_HastaElLimite()
    {
        var (contexto, planes) = Crear(_tenantA);
        await using (contexto)
        {
            async Task Calibrar(Guid tenant, Guid equipo, Guid usuario, DateOnly vence) =>
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.calibracion (tenant_id, equipo_id, registrado_por_id, fecha_calibracion, patron_referencia, fecha_vencimiento) VALUES ({tenant}, {equipo}, {usuario}, {vence.AddYears(-1)}, {"Analizador Fluke"}, {vence})");

            // La más reciente manda: vence en 10 días aunque una anterior ya venció.
            await Calibrar(_tenantA, _equipoA, _ingenieroA, Hoy.AddDays(-200));
            await Calibrar(_tenantA, _equipoA, _ingenieroA, Hoy.AddDays(10));
            await Calibrar(_tenantA, _sinCalibracionA, _ingenieroA, Hoy.AddDays(-5));
            await Calibrar(_tenantA, _deBajaA, _ingenieroA, Hoy.AddDays(-5));
            await Calibrar(_tenantB, _equipoB, _tecnicoB, Hoy.AddDays(-5));

            var hasta20 = await planes.CalibracionesPorVencerAsync(Hoy.AddDays(20), CancellationToken.None);
            var alerta = Assert.Single(hasta20);
            Assert.Equal((_equipoA, "PL-PL1", "Bomba de infusión", "UCI", Hoy.AddDays(10)), (alerta.EquipoId, alerta.PlacaInventario, alerta.TipoEquipo, alerta.Ubicacion, alerta.FechaVencimiento));
            Assert.Empty(await planes.CalibracionesPorVencerAsync(Hoy.AddDays(5), CancellationToken.None));
        }
    }
}
