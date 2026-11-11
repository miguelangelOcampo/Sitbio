using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Catalogos;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioCatalogos"/> contra la base real: cada institución ve y edita solo sus
/// catálogos (RN-008), con los inactivos marcados, los valores regulatorios del tipo (RF-030) y
/// nombres únicos sin distinguir mayúsculas (HU-025).
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
            var nit = Nit();
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba catálogos"}, {nit}, {"Consultorio"}, {300})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.ubicacion (tenant_id, nombre, estado_activo) VALUES ({tenant}, {"Urgencias " + sufijo}, {true}), ({tenant}, {"Bodega " + sufijo}, {false})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tipo_equipo (tenant_id, nombre, clase_riesgo_defecto, requiere_calibracion, periodicidad_calibracion_meses) VALUES ({tenant}, {"Tensiómetro " + sufijo}, {"I"}, {true}, {(short)12}), ({tenant}, {"Estetoscopio " + sufijo}, {"I"}, {false}, {(short?)null})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.proveedor (tenant_id, nombre, nit, tipo_proveedor) VALUES ({tenant}, {"Biomédica " + sufijo}, {nit}, {"Distribuidor"})");
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tipo_falla (tenant_id, nombre) VALUES ({tenant}, {"No enciende"}), ({tenant}, {"Eléctrica " + sufijo})");
        }
    }

    private static string Nit() => $"Q{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    public async Task DisposeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "tipo_falla", "proveedor", "tipo_equipo", "ubicacion" })
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
            Assert.Equal("Distribuidor", proveedor.TipoProveedor);
            Assert.NotNull(proveedor.Nit);
            Assert.Null(proveedor.ClaseRiesgo);
        }
    }

    [FactIntegracion(Variable)]
    public async Task TiposDeFalla_SoloLosDeLaInstitucionOrdenados()
    {
        var (contexto, repositorio) = Crear(_tenantB);
        await using (contexto)
        {
            var tipos = await repositorio.TiposFallaAsync(CancellationToken.None);

            Assert.Equal(["Eléctrica B", "No enciende"], tipos.Select(t => t.Nombre));
            Assert.All(tipos, t => Assert.True(t.Activo));
        }
    }

    // ---------- CA-HU-025-01: lo agregado aparece en el catálogo de la institución ----------

    [FactIntegracion(Variable)]
    public async Task Agregar_CadaCatalogo_QuedaDisponibleSoloEnSuInstitucion()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            repositorio.Agregar(new TipoFalla(_tenantA, "Accesorio dañado"));
            repositorio.Agregar(new Ubicacion(_tenantA, "Neonatos", "Piso 2"));
            repositorio.Agregar(new Proveedor(
                _tenantA, "CalibraLab", Nit(), "Laboratorio de calibración", new ContactoProveedor("Mesa de ayuda", "ayuda@calibralab.co", null)));
            var tipo = new TipoEquipo(_tenantA, "Electrobisturí");
            tipo.DefinirValoresRegulatorios(ClasesRiesgo.IIb, true, 12);
            repositorio.Agregar(tipo);
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (otroContexto, otro) = Crear(_tenantB);
        await using (otroContexto)
        {
            Assert.DoesNotContain(await otro.TiposFallaAsync(CancellationToken.None), t => t.Nombre == "Accesorio dañado");
            Assert.DoesNotContain(await otro.UbicacionesAsync(CancellationToken.None), u => u.Nombre == "Neonatos");
        }

        var (nuevoContexto, releido) = Crear(_tenantA);
        await using (nuevoContexto)
        {
            Assert.Contains(await releido.TiposFallaAsync(CancellationToken.None), t => t.Nombre == "Accesorio dañado" && t.Activo);
            Assert.Contains(await releido.UbicacionesAsync(CancellationToken.None), u => u.Nombre == "Neonatos" && u.Descripcion == "Piso 2");
            Assert.Contains(await releido.ProveedoresAsync(CancellationToken.None), p => p.Nombre == "CalibraLab");
            var electrobisturi = Assert.Single(await releido.TiposEquipoAsync(CancellationToken.None), t => t.Nombre == "Electrobisturí");
            Assert.Equal("IIb", electrobisturi.ClaseRiesgo);

            var guardado = await nuevoContexto.Proveedores.AsNoTracking().SingleAsync(p => p.Nombre == "CalibraLab");
            Assert.Equal(("Mesa de ayuda", "ayuda@calibralab.co"), (guardado.NombreContacto, guardado.Correo));
        }
    }

    // ---------- CA-HU-025-02: inactivar no borra el elemento ----------

    [FactIntegracion(Variable)]
    public async Task Inactivar_SeGuardaYElElementoSigueEnLaLista()
    {
        Guid id;
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            id = (await repositorio.UbicacionesAsync(CancellationToken.None)).Single(u => u.Nombre == "Urgencias A").Id;
            var ubicacion = await repositorio.ObtenerAsync(TiposCatalogo.Ubicaciones, id, CancellationToken.None);
            ubicacion!.Inactivar();
            await repositorio.GuardarAsync(CancellationToken.None);
        }

        var (nuevoContexto, releido) = Crear(_tenantA);
        await using (nuevoContexto)
        {
            var ubicacion = Assert.Single(await releido.UbicacionesAsync(CancellationToken.None), u => u.Id == id);
            Assert.Equal(("Urgencias A", false), (ubicacion.Nombre, ubicacion.Activo));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Obtener_DeOtraInstitucionOCatalogoDesconocido_NoLoEncuentra()
    {
        Guid idDeB;
        var (contextoB, repositorioB) = Crear(_tenantB);
        await using (contextoB)
        {
            idDeB = (await repositorioB.TiposFallaAsync(CancellationToken.None))[0].Id;
            Assert.IsType<TipoFalla>(await repositorioB.ObtenerAsync("Tipos-Falla", idDeB, CancellationToken.None));
        }

        var (contextoA, repositorioA) = Crear(_tenantA);
        await using (contextoA)
        {
            Assert.Null(await repositorioA.ObtenerAsync(TiposCatalogo.TiposFalla, idDeB, CancellationToken.None));
            Assert.Null(await repositorioA.ObtenerAsync("campos", idDeB, CancellationToken.None));
        }
    }

    // ---------- Nombres y NIT únicos por institución ----------

    [FactIntegracion(Variable)]
    public async Task NombreEnUso_SinDistinguirMayusculasYSoloEnLaInstitucion()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.True(await repositorio.NombreEnUsoAsync(TiposCatalogo.Ubicaciones, " urgencias a ", null, CancellationToken.None));
            Assert.True(await repositorio.NombreEnUsoAsync(TiposCatalogo.TiposEquipo, "TENSIÓMETRO A", null, CancellationToken.None));
            Assert.True(await repositorio.NombreEnUsoAsync(TiposCatalogo.Proveedores, "biomédica a", null, CancellationToken.None));
            Assert.True(await repositorio.NombreEnUsoAsync(TiposCatalogo.TiposFalla, "no ENCIENDE", null, CancellationToken.None));
            Assert.False(await repositorio.NombreEnUsoAsync(TiposCatalogo.Ubicaciones, "Urgencias B", null, CancellationToken.None));

            var id = (await repositorio.UbicacionesAsync(CancellationToken.None)).Single(u => u.Nombre == "Urgencias A").Id;
            Assert.False(await repositorio.NombreEnUsoAsync(TiposCatalogo.Ubicaciones, "URGENCIAS A", id, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task NitProveedorEnUso_SoloEnLaInstitucionYExceptoElMismo()
    {
        string nitDeB;
        var (contextoB, _) = Crear(_tenantB);
        await using (contextoB)
        {
            nitDeB = await contextoB.Proveedores.AsNoTracking().Select(p => p.Nit).SingleAsync();
        }

        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var proveedor = await contexto.Proveedores.AsNoTracking().SingleAsync();

            Assert.False(await repositorio.NitProveedorEnUsoAsync(nitDeB, null, CancellationToken.None));
            Assert.True(await repositorio.NitProveedorEnUsoAsync($" {proveedor.Nit} ", null, CancellationToken.None));
            Assert.False(await repositorio.NitProveedorEnUsoAsync(proveedor.Nit, proveedor.Id, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task Guardar_NombreDuplicadoConOtrasMayusculas_RespondeConflicto()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            // Dos registros simultáneos: la consulta previa no lo detecta, el índice lower(nombre) de la V009 sí.
            repositorio.Agregar(new TipoFalla(_tenantA, "NO ENCIENDE"));

            var error = await Assert.ThrowsAsync<ErrorConflicto>(() => repositorio.GuardarAsync(CancellationToken.None));

            Assert.Equal(ErroresCatalogo.CodigoDuplicado, error.Codigo);
            Assert.Contains("ese nombre", error.Message);
        }
    }

    [FactIntegracion(Variable)]
    public async Task Guardar_NitDuplicado_RespondeConflicto()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var nit = await contexto.Proveedores.AsNoTracking().Select(p => p.Nit).SingleAsync();
            repositorio.Agregar(new Proveedor(_tenantA, "Otro proveedor", nit, "Distribuidor"));

            var error = await Assert.ThrowsAsync<ErrorConflicto>(() => repositorio.GuardarAsync(CancellationToken.None));

            Assert.Equal(ErroresCatalogo.CodigoDuplicado, error.Codigo);
            Assert.Contains("ese NIT", error.Message);
        }
    }
}

