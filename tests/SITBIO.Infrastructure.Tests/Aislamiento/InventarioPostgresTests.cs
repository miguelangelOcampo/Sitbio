using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using Sitbio.Application.Comun;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Persistencia.Configuraciones;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// Mapeo del inventario contra la base real (script 01 + V008): la información regulatoria se
/// guarda y se lee, la placa es única por institución (RN-001) y otra institución no ve el equipo.
/// </summary>
public class InventarioPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Dictionary<Guid, (Guid Tipo, Guid Ubicacion)> _catalogos = [];

    private SitbioDbContext CrearContexto(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        return new SitbioDbContext(options, contextoUsuario);
    }

    public async Task InitializeAsync()
    {
        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            await using var contexto = CrearContexto(tenant);
            var nit = $"I{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit) VALUES ({tenant}, {"Prueba inventario"}, {nit})");

            var ubicacion = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {"UCI adultos"})");

            var tipo = new TipoEquipo(tenant, "Monitor de signos vitales");
            tipo.DefinirValoresRegulatorios(ClasesRiesgo.IIb, true, 12);
            contexto.TiposEquipo.Add(tipo);
            await contexto.SaveChangesAsync();

            _catalogos[tenant] = (tipo.Id, ubicacion);
        }
    }

    public async Task DisposeAsync()
    {
        await using var contexto = CrearContexto(_tenantA);
        foreach (var tabla in new[] { "plan_preventivo", "equipo_biomedico", "tipo_equipo", "ubicacion" })
        {
            await contexto.Database.ExecuteSqlRawAsync(
                $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                new NpgsqlParameter("a", _tenantA),
                new NpgsqlParameter("b", _tenantB));
        }

        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
    }

    private EquipoBiomedico NuevoEquipo(Guid tenant, string placa, string serie)
    {
        var (tipo, ubicacion) = _catalogos[tenant];
        return new EquipoBiomedico(tenant, tipo, ubicacion, serie, placa, "Mindray", "BeneView T5", DateTimeOffset.UtcNow);
    }

    [FactIntegracion(Variable)]
    public async Task GuardarEquipoConInformacionRegulatoriaYPlan_SeLeeIgualYOtroTenantNoLoVe()
    {
        var vence = Hoy.AddDays(90);
        var ultima = Hoy.AddMonths(-7);
        Guid equipoId;
        await using (var contextoA = CrearContexto(_tenantA))
        {
            var equipo = NuevoEquipo(_tenantA, "PL-0001", "SN-0001");
            equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "INVIMA 2020DM-0012345", vence, ClasesRiesgo.IIb, true, 6);
            contextoA.Equipos.Add(equipo);
            contextoA.PlanesPreventivos.Add(new PlanPreventivo(_tenantA, equipo.Id, "Preventivo semestral", 180, ultima.AddDays(180), DateTimeOffset.UtcNow, ultima));
            await contextoA.SaveChangesAsync();
            equipoId = equipo.Id;
        }

        await using (var contextoA = CrearContexto(_tenantA))
        {
            var leido = await contextoA.Equipos.SingleAsync(e => e.Id == equipoId);
            Assert.Equal(EstadosEquipo.Operativo, leido.EstadoEquipoId);
            Assert.Equal(TiposAutorizacion.RegistroSanitario, leido.TipoAutorizacion);
            Assert.Equal(vence, leido.VenceRegistro);
            Assert.Equal(ClasesRiesgo.IIb, leido.ClaseRiesgo);
            Assert.Equal((short)6, leido.PeriodicidadCalibracionMeses);
            Assert.Equal(AvisoRegistro.PorVencer, leido.AvisoDelRegistro(Hoy));

            var plan = await contextoA.PlanesPreventivos.SingleAsync(p => p.EquipoId == equipoId);
            Assert.Equal(ultima, plan.UltimaEjecucion);
            Assert.True(plan.EstaVencido(Hoy));

            var estado = await contextoA.EstadosEquipo.SingleAsync(e => e.Id == leido.EstadoEquipoId);
            Assert.False(string.IsNullOrWhiteSpace(estado.Nombre));
        }

        await using var contextoB = CrearContexto(_tenantB);
        Assert.False(await contextoB.Equipos.AnyAsync(e => e.Id == equipoId));
        Assert.False(await contextoB.PlanesPreventivos.AnyAsync(p => p.EquipoId == equipoId));
    }

    [FactIntegracion(Variable)]
    public async Task EquipoSinCalibracion_SeGuardaConPeriodicidadVacia()
    {
        Guid equipoId;
        await using (var contexto = CrearContexto(_tenantA))
        {
            var estetoscopio = NuevoEquipo(_tenantA, "PL-0100", "SN-0100");
            estetoscopio.DefinirInformacionRegulatoria(TiposAutorizacion.PermisoComercializacion, "PC-2021-777", Hoy.AddYears(2), ClasesRiesgo.I, false, null);
            contexto.Equipos.Add(estetoscopio);
            await contexto.SaveChangesAsync();
            equipoId = estetoscopio.Id;
        }

        await using var lectura = CrearContexto(_tenantA);
        var leido = await lectura.Equipos.SingleAsync(e => e.Id == equipoId);
        Assert.False(leido.RequiereCalibracion);
        Assert.Null(leido.PeriodicidadCalibracionMeses);
    }

    [FactIntegracion(Variable)]
    public async Task PlacaRepetidaEnLaMismaInstitucion_ChocaConElIndiceUnico()
    {
        await using (var contexto = CrearContexto(_tenantA))
        {
            contexto.Equipos.Add(NuevoEquipo(_tenantA, "PL-0200", "SN-0200"));
            await contexto.SaveChangesAsync();
        }

        await using var otro = CrearContexto(_tenantA);
        otro.Equipos.Add(NuevoEquipo(_tenantA, "PL-0200", "SN-0201"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => otro.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal(EquipoBiomedicoConfiguracion.IndicePlaca, postgres.ConstraintName);
    }

    [FactIntegracion(Variable)]
    public async Task MismaPlacaEnOtraInstitucion_SeAcepta()
    {
        await using (var contextoA = CrearContexto(_tenantA))
        {
            contextoA.Equipos.Add(NuevoEquipo(_tenantA, "PL-0300", "SN-0300"));
            await contextoA.SaveChangesAsync();
        }

        await using var contextoB = CrearContexto(_tenantB);
        contextoB.Equipos.Add(NuevoEquipo(_tenantB, "PL-0300", "SN-0300"));
        await contextoB.SaveChangesAsync();

        Assert.Equal(1, await contextoB.Equipos.CountAsync(e => e.PlacaInventario == "PL-0300"));
    }
}
