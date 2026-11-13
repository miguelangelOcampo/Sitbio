using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Tiquetes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Infrastructure.Peticiones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioTiquetes"/> contra la base real: consecutivo TQT-###### por institución (también
/// con reportes simultáneos), RN-005 en la misma transacción, tipo de falla del catálogo y alcance por
/// institución y por reportante.
/// </summary>
public class RepositorioTiquetesPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _clinicoA = Guid.NewGuid();
    private readonly Guid _otroClinicoA = Guid.NewGuid();
    private readonly Guid _clinicoB = Guid.NewGuid();
    private Guid _equipoA;
    private Guid _equipoB;

    private (SitbioDbContext Contexto, RepositorioTiquetes Repositorio) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioTiquetes(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        _equipoA = await Preparar(_tenantA, [_clinicoA, _otroClinicoA], "PL-T1");
        _equipoB = await Preparar(_tenantB, [_clinicoB], "PL-T1");
    }

    private async Task<Guid> Preparar(Guid tenant, Guid[] clinicos, string placa)
    {
        var (contexto, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            var ubicacion = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba tiquetes"}, {nit}, {"Consultorio"}, {300})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {tenant}, {"Urgencias"})");
            foreach (var clinico in clinicos)
            {
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.usuario (id_usuario, tenant_id, rol_id, nombres, apellidos, correo, contrasena_hash) SELECT {clinico}, {tenant}, id_rol, {"Laura"}, {"Gómez"}, {$"{clinico:N}@prueba-tiquetes.co"}, {"hash"} FROM sitbio.rol WHERE codigo = {"AC-04"}");
            }

            var tipo = new TipoEquipo(tenant, "Monitor");
            var falla = new TipoFalla(tenant, "No enciende");
            var inactiva = new TipoFalla(tenant, "Ruido");
            inactiva.Inactivar();
            var equipo = new EquipoBiomedico(tenant, tipo.Id, ubicacion, $"SN-{placa}", placa, "Mindray", "uMEC10", DateTimeOffset.UtcNow);
            contexto.AddRange(tipo, falla, inactiva, equipo);
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

        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "historial_tiquete", "tiquete", "historial_estado_equipo", "equipo_biomedico", "tipo_falla", "tipo_equipo", "ubicacion", "usuario" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    /// <summary>Reporta como lo hace GestionTiquetes: equipo con seguimiento y, si es crítica, cambio de estado.</summary>
    private async Task<string> Reportar(Guid tenant, Guid equipoId, Guid reportante, string criticidad = Criticidades.Media)
    {
        var (contexto, repositorio) = Crear(tenant);
        await using (contexto)
        {
            var equipo = (await repositorio.EquipoAsync(equipoId, CancellationToken.None))!;
            var ahora = DateTimeOffset.UtcNow;
            var tiquete = new Tiquete(tenant, equipo.Id, reportante, "No enciende", "Pantalla en negro.", criticidad, ahora);
            var cambio = tiquete.EsCritica ? equipo.CambiarEstado(EstadosEquipo.FueraDeServicio, "Falla crítica (RN-005)", reportante, ahora) : null;
            await repositorio.RegistrarAsync(tiquete, tiquete.Recepcion(), cambio, CancellationToken.None);
            return tiquete.Codigo;
        }
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_NumeraConsecutivoPorInstitucion()
    {
        Assert.Equal("TQT-000001", await Reportar(_tenantA, _equipoA, _clinicoA));
        Assert.Equal("TQT-000002", await Reportar(_tenantA, _equipoA, _otroClinicoA));
        Assert.Equal("TQT-000001", await Reportar(_tenantB, _equipoB, _clinicoB));
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_ReportesSimultaneos_NoRepitenCodigo()
    {
        var codigos = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Reportar(_tenantA, _equipoA, _clinicoA)));

        Assert.Equal(Enumerable.Range(1, 6).Select(Tiquete.Formatear).Order(), codigos.Order());
    }

    [FactIntegracion(Variable)]
    public async Task Registrar_Critica_GuardaTiqueteEquipoEHistorialesJuntos()
    {
        var codigo = await Reportar(_tenantA, _equipoA, _clinicoA, Criticidades.Critica);

        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var leido = (await repositorio.ObtenerAsync(codigo, null, CancellationToken.None))!;
            Assert.Equal(("FUERA_DE_SERVICIO", Criticidades.Critica, EstadosTiquete.Recibido), (leido.EstadoEquipo, leido.Tiquete.Criticidad, leido.Tiquete.Estado));
            Assert.Equal(("PL-T1", "Monitor", "Urgencias", "Laura Gómez"), (leido.PlacaInventario, leido.TipoEquipo, leido.Ubicacion, leido.Reportante));

            var historial = await contexto.HistorialTiquetes.SingleAsync(h => h.TiqueteId == leido.Tiquete.Id);
            Assert.Equal(((string?)null, EstadosTiquete.Recibido, _clinicoA), (historial.EstadoAnterior, historial.NuevoEstado, historial.UsuarioId));
            var cambio = await contexto.HistorialEstados.SingleAsync(h => h.EquipoId == _equipoA);
            Assert.Equal(((short?)EstadosEquipo.Operativo, EstadosEquipo.FueraDeServicio), (cambio.EstadoAnteriorId, cambio.NuevoEstadoId));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Consultas_RespetanInstitucionYReportante()
    {
        var propio = await Reportar(_tenantA, _equipoA, _clinicoA, Criticidades.Critica);
        var ajeno = await Reportar(_tenantA, _equipoA, _otroClinicoA);
        await Reportar(_tenantB, _equipoB, _clinicoB);

        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var (todos, total) = await repositorio.ListarAsync(new FiltroTiquetes(null, null), null, CancellationToken.None);
            Assert.Equal(2, total);
            Assert.Equal([ajeno, propio], todos.Select(t => t.Tiquete.Codigo));

            var (suyos, _) = await repositorio.ListarAsync(new FiltroTiquetes(null, null), _clinicoA, CancellationToken.None);
            Assert.Equal([propio], suyos.Select(t => t.Tiquete.Codigo));

            var (criticos, _) = await repositorio.ListarAsync(new FiltroTiquetes(EstadosTiquete.Recibido, Criticidades.Critica), null, CancellationToken.None);
            Assert.Equal([propio], criticos.Select(t => t.Tiquete.Codigo));

            // Bandeja UI-011: lo crítico primero aunque sea más antiguo.
            var (bandeja, _) = await repositorio.ListarAsync(new FiltroTiquetes(EstadosTiquete.Recibido, null, PorCriticidad: true), null, CancellationToken.None);
            Assert.Equal([propio, ajeno], bandeja.Select(t => t.Tiquete.Codigo));

            Assert.Null(await repositorio.ObtenerAsync(ajeno, _clinicoA, CancellationToken.None));
            Assert.Null(await repositorio.EquipoAsync(_equipoB, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Historial_EnOrdenConQuienHizoCadaCambio_YSoloDeLaInstitucion()
    {
        var codigo = await Reportar(_tenantA, _equipoA, _clinicoA);
        Guid tiqueteId;
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            tiqueteId = (await repositorio.ObtenerAsync(codigo, null, CancellationToken.None))!.Tiquete.Id;

            // La asignación la escribe HU-004; aquí basta con un segundo cambio posterior.
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.historial_tiquete (id_historial, tenant_id, tiquete_id, usuario_id, estado_anterior, nuevo_estado, comentario, fecha_cambio) VALUES ({Guid.NewGuid()}, {_tenantA}, {tiqueteId}, {_otroClinicoA}, {EstadosTiquete.Recibido}, {EstadosTiquete.Asignado}, {"Asignado a técnico"}, {DateTimeOffset.UtcNow.AddMinutes(5)})");

            var historial = await repositorio.HistorialAsync(tiqueteId, CancellationToken.None);

            Assert.Equal(
                [((string?)null, EstadosTiquete.Recibido, _clinicoA), (EstadosTiquete.Recibido, EstadosTiquete.Asignado, _otroClinicoA)],
                historial.Select(h => (h.Cambio.EstadoAnterior, h.Cambio.NuevoEstado, h.Cambio.UsuarioId)));
            Assert.All(historial, h => Assert.Equal("Laura Gómez", h.Usuario));
        }

        var (otro, repositorioB) = Crear(_tenantB);
        await using (otro)
        {
            Assert.Empty(await repositorioB.HistorialAsync(tiqueteId, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task TipoFalla_SoloActivosYSinDistinguirMayusculas()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal("No enciende", await repositorio.TipoFallaActivoAsync("NO ENCIENDE", CancellationToken.None));
            Assert.Null(await repositorio.TipoFallaActivoAsync("Ruido", CancellationToken.None));
            Assert.Null(await repositorio.TipoFallaActivoAsync("Se cayó", CancellationToken.None));
            Assert.Equal(["No enciende"], await repositorio.TiposFallaActivosAsync(CancellationToken.None));
        }
    }
}
