using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Mantenimiento;
using Sitbio.Infrastructure.Peticiones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioOrdenes"/> contra la base real (HU-004): la orden, su historial y el tiquete
/// «asignado» se guardan juntos; el técnico candidato se lee con rol y estado (RN-003) solo dentro de la
/// institución; un tiquete no recibe dos órdenes ni con asignaciones simultáneas.
/// </summary>
public class RepositorioOrdenesPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _ingenieroA = Guid.NewGuid();
    private readonly Guid _tecnicoA = Guid.NewGuid();
    private readonly Guid _otroTecnicoA = Guid.NewGuid();
    private readonly Guid _tecnicoInactivoA = Guid.NewGuid();
    private readonly Guid _tecnicoB = Guid.NewGuid();
    private Guid _equipoA;

    private (SitbioDbContext Contexto, RepositorioOrdenes Ordenes, RepositorioTiquetes Tiquetes) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioOrdenes(contexto), new RepositorioTiquetes(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        _equipoA = await Preparar(_tenantA, [
            (_ingenieroA, CodigosRol.IngenieroBiomedico, true),
            (_tecnicoA, CodigosRol.TecnicoMantenimiento, true),
            (_otroTecnicoA, CodigosRol.TecnicoMantenimiento, true),
            (_tecnicoInactivoA, CodigosRol.TecnicoMantenimiento, false),
        ]);
        await Preparar(_tenantB, [(_tecnicoB, CodigosRol.TecnicoMantenimiento, true)]);
    }

    private async Task<Guid> Preparar(Guid tenant, (Guid Id, string Rol, bool Activo)[] usuarios)
    {
        var (contexto, _, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            var ubicacion = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba órdenes"}, {nit}, {"Consultorio"}, {300})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {"Urgencias"})");
            foreach (var (id, rol, activo) in usuarios)
            {
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.usuario (id_usuario, tenant_id, rol_id, nombres, apellidos, correo, contrasena_hash, estado_activo) SELECT {id}, {tenant}, id_rol, {"Carlos"}, {"Ruiz"}, {$"{id:N}@prueba-ordenes.co"}, {"hash"}, {activo} FROM sitbio.rol WHERE codigo = {rol}");
            }

            var tipo = new TipoEquipo(tenant, "Monitor");
            var equipo = new EquipoBiomedico(tenant, tipo.Id, ubicacion, "SN-OT1", "PL-OT1", "Mindray", "uMEC10", DateTimeOffset.UtcNow);
            contexto.AddRange(tipo, equipo);
            await contexto.SaveChangesAsync();
            return equipo.Id;
        }
    }

    public async Task DisposeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        var (contexto, _, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "orden_trabajo", "historial_tiquete", "tiquete", "historial_estado_equipo", "equipo_biomedico", "tipo_equipo", "ubicacion", "usuario" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    private async Task<Guid> Reportar(string criticidad = Criticidades.Alta)
    {
        var (contexto, _, tiquetes) = Crear(_tenantA);
        await using (contexto)
        {
            var tiquete = new Tiquete(_tenantA, _equipoA, _ingenieroA, "No enciende", null, criticidad, DateTimeOffset.UtcNow);
            await tiquetes.RegistrarAsync(tiquete, tiquete.Recepcion(), null, CancellationToken.None);
            return tiquete.Id;
        }
    }

    /// <summary>Asigna como lo hace GestionOrdenes: tiquete con seguimiento, orden e historial en un solo guardado.</summary>
    private async Task<Guid> Asignar(Guid tiqueteId, Guid tecnico, DateTimeOffset? ahora = null)
    {
        var (contexto, ordenes, _) = Crear(_tenantA);
        await using (contexto)
        {
            var tiquete = (await ordenes.TiqueteAsync(tiqueteId, CancellationToken.None))!;
            var (orden, cambio) = OrdenTrabajo.Correctiva(tiquete, tecnico, _ingenieroA, null, null, ahora ?? DateTimeOffset.UtcNow);
            await ordenes.RegistrarAsync(orden, cambio, CancellationToken.None);
            return orden.Id;
        }
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_GuardaLaOrdenElHistorialYElTiqueteAsignadoJuntos()
    {
        var tiqueteId = await Reportar(Criticidades.Critica);

        var ordenId = await Asignar(tiqueteId, _tecnicoA);

        var (contexto, ordenes, _) = Crear(_tenantA);
        await using (contexto)
        {
            var leida = (await ordenes.ObtenerAsync(ordenId, null, CancellationToken.None))!;
            Assert.Equal((EstadosOrden.Asignada, TiposMantenimiento.Correctivo, Criticidades.Critica), (leida.Orden.Estado, leida.Orden.TipoMantenimiento, leida.Orden.Prioridad));
            Assert.Equal(("PL-OT1", "Monitor", "Urgencias", "Carlos Ruiz"), (leida.PlacaInventario, leida.TipoEquipo, leida.Ubicacion, leida.Tecnico));
            Assert.Equal(("TQT-000001", "No enciende"), (leida.CodigoTiquete, leida.TipoFalla));

            var tiquete = await contexto.Tiquetes.AsNoTracking().SingleAsync(t => t.Id == tiqueteId);
            Assert.Equal(EstadosTiquete.Asignado, tiquete.Estado);
            var cambios = await contexto.HistorialTiquetes.AsNoTracking().Where(h => h.TiqueteId == tiqueteId).Select(h => h.NuevoEstado).ToListAsync();
            Assert.Equal([EstadosTiquete.Asignado, EstadosTiquete.Recibido], cambios.Order(StringComparer.Ordinal));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Tecnico_TraeRolYEstadoSoloDentroDeLaInstitucion()
    {
        var (contexto, ordenes, _) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(new TecnicoCandidato(_tecnicoA, CodigosRol.TecnicoMantenimiento, true), await ordenes.TecnicoAsync(_tecnicoA, CancellationToken.None));
            Assert.Equal(new TecnicoCandidato(_tecnicoInactivoA, CodigosRol.TecnicoMantenimiento, false), await ordenes.TecnicoAsync(_tecnicoInactivoA, CancellationToken.None));
            Assert.Equal(CodigosRol.IngenieroBiomedico, (await ordenes.TecnicoAsync(_ingenieroA, CancellationToken.None))!.CodigoRol);
            Assert.Null(await ordenes.TecnicoAsync(_tecnicoB, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Listar_FiltraPorTecnicoYOrdenaPorPrioridad()
    {
        var baja = await Asignar(await Reportar(Criticidades.Baja), _tecnicoA);
        var critica = await Asignar(await Reportar(Criticidades.Critica), _tecnicoA);
        var deOtro = await Asignar(await Reportar(Criticidades.Alta), _otroTecnicoA);

        var (contexto, ordenes, _) = Crear(_tenantA);
        await using (contexto)
        {
            var (suyas, totalSuyas) = await ordenes.ListarAsync(new FiltroOrdenes(null), _tecnicoA, CancellationToken.None);
            Assert.Equal([critica, baja], suyas.Select(o => o.Orden.Id));
            Assert.Equal(2, totalSuyas);

            var (todas, total) = await ordenes.ListarAsync(new FiltroOrdenes(EstadosOrden.Asignada), null, CancellationToken.None);
            Assert.Equal([critica, deOtro, baja], todas.Select(o => o.Orden.Id));
            Assert.Equal(3, total);

            Assert.Null(await ordenes.ObtenerAsync(deOtro, _tecnicoA, CancellationToken.None));
            Assert.Empty((await ordenes.ListarAsync(new FiltroOrdenes(EstadosOrden.Cerrada), null, CancellationToken.None)).Items);
        }

        var (otraInstitucion, ordenesB, _) = Crear(_tenantB);
        await using (otraInstitucion)
        {
            Assert.Equal(0, (await ordenesB.ListarAsync(new FiltroOrdenes(null), null, CancellationToken.None)).Total);
        }
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_AsignacionesSimultaneasDelMismoTiquete_SoloUnaGana()
    {
        var tiqueteId = await Reportar();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await Asignar(tiqueteId, _tecnicoA);
                return null;
            }
            catch (ErrorConflicto error)
            {
                return error.Codigo;
            }
        }));

        Assert.Single(resultados, r => r is null);
        Assert.All(resultados.Where(r => r is not null), codigo => Assert.Equal(GestionOrdenes.CodigoTiqueteAsignado, codigo));
        var (contexto, _, _) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(1, await contexto.Ordenes.CountAsync(o => o.TiqueteId == tiqueteId));
        }
    }
}
