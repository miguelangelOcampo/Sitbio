using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Inventario;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// Consultas de <see cref="RepositorioEquipos"/> contra la base real: filtros de API-EQ-LIST,
/// nombres y plan activo, capacidad del plan (RN-009) y placa única (RN-001) por institución.
/// </summary>
public class RepositorioEquiposPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _ubicacionA = Guid.NewGuid();
    private readonly Guid _ubicacionB = Guid.NewGuid();
    private TipoEquipo _tipoA = null!;
    private TipoEquipo _tipoB = null!;

    private (SitbioDbContext Contexto, RepositorioEquipos Repositorio) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioEquipos(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        _tipoA = await Preparar(_tenantA, _ubicacionA);
        _tipoB = await Preparar(_tenantB, _ubicacionB);

        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var monitor = Equipo(_tenantA, _tipoA, _ubicacionA, "AB-01", ClasesRiesgo.IIb, true);
            repositorio.Agregar(monitor);
            repositorio.Agregar(new PlanPreventivo(_tenantA, monitor.Id, "Semestral", 180, Hoy.AddDays(30), DateTimeOffset.UtcNow, Hoy.AddDays(-150)));
            repositorio.Agregar(Equipo(_tenantA, _tipoA, _ubicacionA, "ab-02", ClasesRiesgo.I, false));
            repositorio.Agregar(Equipo(_tenantA, _tipoA, _ubicacionA, "XY_03", ClasesRiesgo.I, true));
            repositorio.Agregar(Equipo(_tenantA, _tipoA, _ubicacionA, "XYZ04", ClasesRiesgo.III, true));
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            repositorioB.Agregar(Equipo(_tenantB, _tipoB, _ubicacionB, "AB-01", ClasesRiesgo.IIb, true));
            await repositorioB.GuardarAsync(CancellationToken.None);
        }
    }

    private async Task<TipoEquipo> Preparar(Guid tenant, Guid ubicacion)
    {
        var (contexto, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"Q{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba equipos"}, {nit}, {"Consultorio"}, {300})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {"Urgencias"})");
            var tipo = new TipoEquipo(tenant, "Monitor");
            contexto.TiposEquipo.Add(tipo);
            await contexto.SaveChangesAsync();
            return tipo;
        }
    }

    private static EquipoBiomedico Equipo(Guid tenant, TipoEquipo tipo, Guid ubicacion, string placa, string clase, bool calibracion)
    {
        var equipo = new EquipoBiomedico(tenant, tipo.Id, ubicacion, $"SN-{placa}", placa, "Mindray", "T5", DateTimeOffset.UtcNow);
        equipo.DefinirInformacionRegulatoria(TiposAutorizacion.RegistroSanitario, "R-1", Hoy.AddYears(1), clase, calibracion, calibracion ? 12 : null);
        return equipo;
    }

    public async Task DisposeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "historial_estado_equipo", "plan_preventivo", "equipo_biomedico", "tipo_equipo", "ubicacion", "usuario" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    private async Task<string[]> Placas(FiltroEquipos filtro)
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var (items, total) = await repositorio.ListarAsync(filtro, CancellationToken.None);
            Assert.Equal(items.Count, total);
            // El orden de la base depende de su intercalación; aquí solo importa qué equipos llegan.
            return items.Select(i => i.Equipo.PlacaInventario).Order(StringComparer.Ordinal).ToArray();
        }
    }

    [FactIntegracion(Variable)]
    public async Task Listar_FiltraSoloLosEquiposDeLaInstitucion()
    {
        Assert.Equal(["AB-01", "XYZ04", "XY_03", "ab-02"], await Placas(new FiltroEquipos()));
        Assert.Equal(["AB-01", "ab-02"], await Placas(new FiltroEquipos(Placa: "ab")));
        Assert.Equal(["XY_03"], await Placas(new FiltroEquipos(Placa: "y_")));
        Assert.Equal(["ab-02"], await Placas(new FiltroEquipos(Serie: "sn-ab-02")));
        Assert.Equal(["XY_03", "ab-02"], await Placas(new FiltroEquipos(Clase: ClasesRiesgo.I)));
        Assert.Equal(["ab-02"], await Placas(new FiltroEquipos(Calibracion: false)));
        Assert.Equal(["XY_03"], await Placas(new FiltroEquipos(Clase: ClasesRiesgo.I, Calibracion: true)));
        Assert.Empty(await Placas(new FiltroEquipos(Estado: "BAJA")));
        Assert.Equal(4, (await Placas(new FiltroEquipos(Estado: EstadosEquipo.CodigoOperativo))).Length);
    }

    [FactIntegracion(Variable)]
    public async Task Listar_PaginaYTraeNombresYPlanActivo()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var (items, total) = await repositorio.ListarAsync(new FiltroEquipos(Pagina: 1, Tamano: 1), CancellationToken.None);

            Assert.Equal(4, total);
            var monitor = Assert.Single(items);
            Assert.Equal("Monitor", monitor.TipoEquipo);
            Assert.Equal("Urgencias", monitor.Ubicacion);
            Assert.Equal(EstadosEquipo.CodigoOperativo, monitor.EstadoCodigo);
            Assert.Equal(Hoy.AddDays(-150), monitor.Plan!.UltimaEjecucion);
        }
    }

    [FactIntegracion(Variable)]
    public async Task ObtenerParaEditar_GuardaLosCambiosYOtroTenantNoLoVe()
    {
        Guid id;
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            id = (await repositorio.ListarAsync(new FiltroEquipos(Placa: "XYZ04"), CancellationToken.None)).Items[0].Equipo.Id;
            var leido = await repositorio.ObtenerAsync(id, paraEditar: true, CancellationToken.None);
            leido!.Equipo.ActualizarDatos(_tipoA.Id, _ubicacionA, "SN-NUEVA", "XYZ04", "Philips", "MX450", null, null);
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (lectura, repositorioLectura) = Crear(_tenantA);
        await using (lectura)
        {
            var leido = await repositorioLectura.ObtenerAsync(id, paraEditar: false, CancellationToken.None);
            Assert.Equal("Philips", leido!.Equipo.Marca);
            Assert.Null(leido.Plan);
        }

        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            Assert.Null(await repositorioB.ObtenerAsync(id, paraEditar: false, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Capacidad_CuentaSoloLosEquiposQueNoEstanDeBaja()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            await contexto.Database.ExecuteSqlAsync(
                $"UPDATE sitbio.equipo_biomedico SET estado_equipo_id = 4, fecha_baja = now() WHERE tenant_id = {_tenantA} AND placa_inventario = {"XYZ04"}");

            var capacidad = await repositorio.CapacidadEquiposAsync(CancellationToken.None);

            Assert.Equal(new CapacidadEquipos("Consultorio", 300, 3), capacidad);
        }
    }

    [FactIntegracion(Variable)]
    public async Task Duplicados_PorInstitucionYGuardarTraduceElIndiceUnicoARn001()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(new Duplicados(false, true), await repositorio.DuplicadosAsync("SN-otra", "AB-01", null, CancellationToken.None));
            Assert.Equal(new Duplicados(true, false), await repositorio.DuplicadosAsync("SN-AB-01", "otra", null, CancellationToken.None));

            repositorio.Agregar(Equipo(_tenantA, _tipoA, _ubicacionA, "AB-01", ClasesRiesgo.I, false));
            var error = await Assert.ThrowsAsync<ErrorConflicto>(() => repositorio.GuardarAsync(CancellationToken.None));
            Assert.Equal(GestionEquipos.CodigoDuplicado, error.Codigo);
        }

        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            Assert.Equal(new Duplicados(false, false), await repositorioB.DuplicadosAsync("SN-XY_03", "XY_03", null, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Historial_GuardaCadaCambioDelMasRecienteAlMasAntiguoYOtroTenantNoLoVe()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        Guid equipoId;
        await using (contexto)
        {
            var ingeniero = new Usuario(_tenantA, 2, "Ingeniero", "De Prueba", $"historial.{Guid.NewGuid():N}@demo-a.co", "hash-de-prueba");
            contexto.Usuarios.Add(ingeniero);
            var equipo = (await repositorio.ListarAsync(new FiltroEquipos { Placa = "XY_03" }, CancellationToken.None)).Items.Single().Equipo;
            equipoId = equipo.Id;
            var editable = (await repositorio.ObtenerAsync(equipoId, paraEditar: true, CancellationToken.None))!.Equipo;
            var momento = DateTimeOffset.UtcNow;

            repositorio.Agregar(HistorialEstadoEquipo.Alta(editable, ingeniero.Id));
            repositorio.Agregar(editable.DarDeBaja("Obsoleto, sin repuestos del fabricante.", ingeniero.Id, momento.AddMinutes(1)));
            repositorio.Agregar(editable.RevertirBaja("Se consiguieron los repuestos.", ingeniero.Id, momento.AddMinutes(2)));
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (lectura, repositorioLectura) = Crear(_tenantA);
        await using (lectura)
        {
            var historial = await repositorioLectura.HistorialAsync(equipoId, CancellationToken.None);

            Assert.Equal(
                [(EstadosEquipo.Baja, EstadosEquipo.Operativo), (EstadosEquipo.Operativo, EstadosEquipo.Baja), ((short?)null, EstadosEquipo.Operativo)],
                historial.Select(h => (h.EstadoAnteriorId, h.NuevoEstadoId)).ToArray());
            Assert.Equal("Se consiguieron los repuestos.", historial[0].Motivo);
            Assert.Null(historial[2].Motivo);

            var equipo = (await repositorioLectura.ObtenerAsync(equipoId, paraEditar: false, CancellationToken.None))!.Equipo;
            Assert.False(equipo.DadoDeBaja);
            Assert.Equal(EstadosEquipo.Operativo, equipo.EstadoEquipoId);
        }

        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            Assert.Empty(await repositorioB.HistorialAsync(equipoId, CancellationToken.None));
        }
    }
}
