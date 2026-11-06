using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Catalogos;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioCatalogos"/> contra la base real: cada institución ve solo sus catálogos
/// (RN-008), con los inactivos marcados y los valores regulatorios del tipo (RF-030).
/// </summary>
public class RepositorioCatalogosPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private (SitbioDbContext Contexto, RepositorioCatalogos Repositorio) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioCatalogos(contexto));
    }

    public async Task InitializeAsync()
    {
        await Preparar(_tenantA, "A");
        await Preparar(_tenantB, "B");
    }

    private async Task Preparar(Guid tenant, string sufijo)
    {
        var (contexto, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"Q{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba catálogos"}, {nit}, {"Consultorio"}, {300})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (tenant_id, nombre, estado_activo) VALUES ({tenant}, {"Urgencias " + sufijo}, {true}), ({tenant}, {"Bodega " + sufijo}, {false})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tipo_equipo (tenant_id, nombre, clase_riesgo_defecto, requiere_calibracion, periodicidad_calibracion_meses) VALUES ({tenant}, {"Tensiómetro " + sufijo}, {"I"}, {true}, {(short)12}), ({tenant}, {"Estetoscopio " + sufijo}, {"I"}, {false}, {(short?)null})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.proveedor (tenant_id, nombre, nit, tipo_proveedor) VALUES ({tenant}, {"Biomédica " + sufijo}, {nit}, {"Distribuidor"})");
        }
    }

    public async Task DisposeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "proveedor", "tipo_equipo", "ubicacion" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    [FactIntegracion(Variable)]
    public async Task Ubicaciones_SoloLasDeLaInstitucionYConSuEstado()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var ubicaciones = await repositorio.UbicacionesAsync(CancellationToken.None);

            Assert.Equal(["Bodega A", "Urgencias A"], ubicaciones.Select(u => u.Nombre).Order(StringComparer.Ordinal));
            Assert.False(ubicaciones.Single(u => u.Nombre == "Bodega A").Activo);
        }
    }

    [FactIntegracion(Variable)]
    public async Task TiposDeEquipo_TraenLosValoresRegulatoriosPorDefecto()
    {
        var (contexto, repositorio) = Crear(_tenantB);
        await using (contexto)
        {
            var tipos = await repositorio.TiposEquipoAsync(CancellationToken.None);

            Assert.Equal(2, tipos.Count);
            var tensiometro = tipos.Single(t => t.Nombre == "Tensiómetro B");
            Assert.Equal(("I", true, 12), (tensiometro.ClaseRiesgo, tensiometro.RequiereCalibracion, tensiometro.PeriodicidadCalibracionMeses));
            var estetoscopio = tipos.Single(t => t.Nombre == "Estetoscopio B");
            Assert.Equal((false, (int?)null), (estetoscopio.RequiereCalibracion!.Value, estetoscopio.PeriodicidadCalibracionMeses));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Proveedores_SoloLosDeLaInstitucion()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var proveedor = Assert.Single(await repositorio.ProveedoresAsync(CancellationToken.None));

            Assert.Equal("Biomédica A", proveedor.Nombre);
            Assert.Null(proveedor.ClaseRiesgo);
        }
    }
}
