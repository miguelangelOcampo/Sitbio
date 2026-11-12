using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Instituciones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioInstitucion"/> contra la base real (HU-024): solo el tenant del token se
/// lee y se modifica (RN-008, CA-HU-024-02) y el consumo sigue las reglas de RN-009.
/// </summary>
public class RepositorioInstitucionPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly string _nitA = Nit();
    private readonly string _nitB = Nit();

    private static string Nit()
    {
        var n = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{n[..3]}.{n[3..6]}.{n[6..]}-{Random.Shared.Next(10)}";
    }

    private (SitbioDbContext Contexto, RepositorioInstitucion Repositorio) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioInstitucion(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_usuarios_gestion, max_equipos, max_almacenamiento_gb, vigente_hasta, dias_anticipacion_alerta) VALUES ({_tenantA}, {"Prueba institución A"}, {_nitA}, {"Consultorio"}, {3}, {300}, {5}, {new DateOnly(2027, 6, 30)}, {(short)15})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_usuarios_gestion, max_equipos, max_almacenamiento_gb) VALUES ({_tenantB}, {"Prueba institución B"}, {_nitB}, {"Hospital"}, {14}, {3000}, {100})");

            // Tenant A: AC-02 activo (cuenta), AC-04 activo y AC-03 inactivo (no cuentan).
            var ingeniero = await Usuario(contexto, _tenantA, "AC-02", activo: true);
            await Usuario(contexto, _tenantA, "AC-04", activo: true);
            await Usuario(contexto, _tenantA, "AC-03", activo: false);
            await Usuario(contexto, _tenantB, "AC-02", activo: true);

            // Tenant A: un equipo operativo y uno de baja (no cuenta); el operativo tiene dos documentos.
            var tipo = Guid.NewGuid();
            var ubicacion = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync($"INSERT INTO sitbio.tipo_equipo (id_tipo_equipo, tenant_id, nombre) VALUES ({tipo}, {_tenantA}, {"Monitor"})");
            await contexto.Database.ExecuteSqlAsync($"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({ubicacion}, {_tenantA}, {"UCI"})");
            var operativo = Guid.NewGuid();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.equipo_biomedico (id_equipo, tenant_id, tipo_equipo_id, ubicacion_id, estado_equipo_id, numero_serie, placa_inventario, marca, modelo) VALUES ({operativo}, {_tenantA}, {tipo}, {ubicacion}, {(short)1}, {"S-1"}, {"P-1"}, {"Philips"}, {"MX450"}), ({Guid.NewGuid()}, {_tenantA}, {tipo}, {ubicacion}, {(short)4}, {"S-2"}, {"P-2"}, {"Philips"}, {"MX450"})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.documento_equipo (tenant_id, equipo_id, subido_por_id, tipo_documento, nombre_archivo, ruta_almacenamiento, tipo_mime, tamano_bytes) VALUES ({_tenantA}, {operativo}, {ingeniero}, {"Manual"}, {"manual.pdf"}, {"a/1"}, {"application/pdf"}, {1_000_000L}), ({_tenantA}, {operativo}, {ingeniero}, {"Ficha técnica"}, {"ficha.pdf"}, {"a/2"}, {"application/pdf"}, {500_000L})");
        }
    }

    private static async Task<Guid> Usuario(SitbioDbContext contexto, Guid tenant, string codigoRol, bool activo)
    {
        var id = Guid.NewGuid();
        await contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.usuario (id_usuario, tenant_id, rol_id, nombres, apellidos, correo, contrasena_hash, estado_activo) SELECT {id}, {tenant}, id_rol, {"Prueba"}, {codigoRol}, {$"{id:N}@prueba-institucion.co"}, {"hash"}, {activo} FROM sitbio.rol WHERE codigo = {codigoRol}");
        return id;
    }

    public async Task DisposeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            var a = _tenantA;
            var b = _tenantB;
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.documento_equipo WHERE tenant_id IN ({a}, {b})");
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.equipo_biomedico WHERE tenant_id IN ({a}, {b})");
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tipo_equipo WHERE tenant_id IN ({a}, {b})");
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.ubicacion WHERE tenant_id IN ({a}, {b})");
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.usuario WHERE tenant_id IN ({a}, {b})");
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({a}, {b})");
        }
    }

    [FactIntegracion(Variable)]
    public async Task Obtener_DevuelveSoloElTenantDelTokenConSusDatosYPlan()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var tenant = await repositorio.ObtenerAsync(CancellationToken.None);

            Assert.Equal(_tenantA, tenant!.Id);
            Assert.Equal(("Prueba institución A", _nitA, (short)15), (tenant.Nombre, tenant.Nit, tenant.DiasAnticipacionAlerta));
            Assert.Equal(("Consultorio", 3, 300, 5), (tenant.Plan, tenant.MaxUsuariosGestion, tenant.MaxEquipos, tenant.MaxAlmacenamientoGb));
            Assert.Equal(new DateOnly(2027, 6, 30), tenant.VigenteHasta);
        }
    }

    [FactIntegracion(Variable)]
    public async Task Uso_CuentaSoloGestionActivosEquiposNoDeBajaYDocumentosDelTenant()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var uso = await repositorio.UsoAsync(CancellationToken.None);

            Assert.Equal(1, uso.UsuariosGestion);
            Assert.Equal(1, uso.EquiposActivos);
            Assert.Equal(1_500_000L, uso.BytesDocumentos);
        }

        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            Assert.Equal(new Application.Instituciones.UsoPlan(1, 0, 0), await repositorioB.UsoAsync(CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task CA_HU_024_02_Guardar_SoloCambiaElTenantDelToken()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var tenant = await repositorio.ObtenerAsync(CancellationToken.None);
            tenant!.ActualizarDatos("Prueba institución A editada", _nitA, 30);
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (lectura, _) = Crear(_tenantA);
        await using (lectura)
        {
            var filas = await lectura.Tenants.AsNoTracking()
                .Where(t => t.Id == _tenantA || t.Id == _tenantB)
                .ToDictionaryAsync(t => t.Id, t => (t.Nombre, t.DiasAnticipacionAlerta));

            Assert.Equal(("Prueba institución A editada", (short)30), filas[_tenantA]);
            Assert.Equal(("Prueba institución B", (short)15), filas[_tenantB]);
        }
    }

    [FactIntegracion(Variable)]
    public async Task NitEnUso_SoloSiLoTieneOtraInstitucion()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.True(await repositorio.NitEnUsoAsync(_nitB, CancellationToken.None));
            Assert.False(await repositorio.NitEnUsoAsync(_nitA, CancellationToken.None));
            Assert.False(await repositorio.NitEnUsoAsync("000.000.000-0", CancellationToken.None));
        }
    }
}
