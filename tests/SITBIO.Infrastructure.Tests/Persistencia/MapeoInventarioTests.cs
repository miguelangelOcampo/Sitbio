using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Persistencia.Configuraciones;

namespace Sitbio.Infrastructure.Tests.Persistencia;

/// <summary>Nombres de tablas, columnas e índices del inventario según el script 01, la V008 y la V009; no abre conexión.</summary>
public class MapeoInventarioTests
{
    private static IModel Modelo()
    {
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql("Host=localhost;Database=sitbio_pruebas").Options;
        using var contexto = new SitbioDbContext(options, Substitute.For<IContextoUsuario>());
        return contexto.Model;
    }

    private static string Columna(IEntityType tipo, string propiedad) =>
        tipo.FindProperty(propiedad)!.GetColumnName(StoreObjectIdentifier.Table(tipo.GetTableName()!, tipo.GetSchema()));

    [Theory]
    [InlineData(typeof(EstadoEquipo), "estado_equipo")]
    [InlineData(typeof(TipoEquipo), "tipo_equipo")]
    [InlineData(typeof(Ubicacion), "ubicacion")]
    [InlineData(typeof(Proveedor), "proveedor")]
    [InlineData(typeof(TipoFalla), "tipo_falla")]
    [InlineData(typeof(EquipoBiomedico), "equipo_biomedico")]
    [InlineData(typeof(PlanPreventivo), "plan_preventivo")]
    public void Entidad_SeMapeaASuTablaEnElEsquemaSitbio(Type entidad, string tabla)
    {
        var tipo = Modelo().FindEntityType(entidad)!;

        Assert.Equal(tabla, tipo.GetTableName());
        Assert.Equal(SitbioDbContext.Esquema, tipo.GetSchema());
    }

    [Fact]
    public void Equipo_ColumnasDelScript01YDeLaV008()
    {
        var equipo = Modelo().FindEntityType(typeof(EquipoBiomedico))!;

        Assert.Equal("id_equipo", Columna(equipo, nameof(EquipoBiomedico.Id)));
        Assert.Equal("tenant_id", Columna(equipo, nameof(EquipoBiomedico.TenantId)));
        Assert.Equal("estado_equipo_id", Columna(equipo, nameof(EquipoBiomedico.EstadoEquipoId)));
        Assert.Equal("placa_inventario", Columna(equipo, nameof(EquipoBiomedico.PlacaInventario)));
        Assert.Equal("numero_serie", Columna(equipo, nameof(EquipoBiomedico.NumeroSerie)));
        Assert.Equal("tipo_autorizacion", Columna(equipo, nameof(EquipoBiomedico.TipoAutorizacion)));
        Assert.Equal("registro_invima", Columna(equipo, nameof(EquipoBiomedico.RegistroInvima)));
        Assert.Equal("vence_registro", Columna(equipo, nameof(EquipoBiomedico.VenceRegistro)));
        Assert.Equal("clase_riesgo", Columna(equipo, nameof(EquipoBiomedico.ClaseRiesgo)));
        Assert.Equal("periodicidad_calibracion_meses", Columna(equipo, nameof(EquipoBiomedico.PeriodicidadCalibracionMeses)));
        Assert.Equal("foto_documento_id", Columna(equipo, nameof(EquipoBiomedico.FotoDocumentoId)));
        Assert.Null(equipo.FindProperty(nameof(EquipoBiomedico.DadoDeBaja)));
    }

    [Fact]
    public void TipoYPlan_ColumnasDeLaV008()
    {
        var modelo = Modelo();
        var tipo = modelo.FindEntityType(typeof(TipoEquipo))!;
        var plan = modelo.FindEntityType(typeof(PlanPreventivo))!;

        Assert.Equal("id_tipo_equipo", Columna(tipo, nameof(TipoEquipo.Id)));
        Assert.Equal("clase_riesgo_defecto", Columna(tipo, nameof(TipoEquipo.ClaseRiesgoDefecto)));
        Assert.Equal("requiere_calibracion", Columna(tipo, nameof(TipoEquipo.RequiereCalibracion)));
        Assert.Equal("id_plan", Columna(plan, nameof(PlanPreventivo.Id)));
        Assert.Equal("ultima_ejecucion", Columna(plan, nameof(PlanPreventivo.UltimaEjecucion)));
        Assert.Equal("proxima_fecha", Columna(plan, nameof(PlanPreventivo.ProximaFecha)));
    }

    [Theory]
    [InlineData(EquipoBiomedicoConfiguracion.IndicePlaca, nameof(EquipoBiomedico.PlacaInventario))]
    [InlineData(EquipoBiomedicoConfiguracion.IndiceSerie, nameof(EquipoBiomedico.NumeroSerie))]
    public void Equipo_PlacaYSerieUnicasPorTenant(string indice, string propiedad)
    {
        var equipo = Modelo().FindEntityType(typeof(EquipoBiomedico))!;

        var unico = Assert.Single(equipo.GetIndexes(), i => i.GetDatabaseName() == indice);

        Assert.True(unico.IsUnique);
        Assert.Equal([nameof(EquipoBiomedico.TenantId), propiedad], unico.Properties.Select(p => p.Name));
    }

    [Fact]
    public void FiltroPorTenant_EnTodoMenosElCatalogoDeEstados()
    {
        var modelo = Modelo();

        foreach (var entidad in new[] { typeof(TipoEquipo), typeof(Ubicacion), typeof(Proveedor), typeof(TipoFalla), typeof(EquipoBiomedico), typeof(PlanPreventivo) })
        {
            Assert.NotEmpty(modelo.FindEntityType(entidad)!.GetDeclaredQueryFilters());
        }

        Assert.Empty(modelo.FindEntityType(typeof(EstadoEquipo))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Proveedor_DatosDeContactoDelScript01()
    {
        var proveedor = Modelo().FindEntityType(typeof(Proveedor))!;

        Assert.Equal("nombre_contacto", Columna(proveedor, nameof(Proveedor.NombreContacto)));
        Assert.Equal("correo", Columna(proveedor, nameof(Proveedor.Correo)));
        Assert.Equal("telefono", Columna(proveedor, nameof(Proveedor.Telefono)));
    }

    [Fact]
    public void TipoFalla_ColumnasDeLaV009()
    {
        var tipo = Modelo().FindEntityType(typeof(TipoFalla))!;

        Assert.Equal("id_tipo_falla", Columna(tipo, nameof(TipoFalla.Id)));
        Assert.Equal("tenant_id", Columna(tipo, nameof(TipoFalla.TenantId)));
        Assert.Equal("nombre", Columna(tipo, nameof(TipoFalla.Nombre)));
        Assert.Equal("estado_activo", Columna(tipo, nameof(TipoFalla.EstadoActivo)));
        Assert.Equal(TipoFalla.LongitudMaximaNombre, tipo.FindProperty(nameof(TipoFalla.Nombre))!.GetMaxLength());
    }

    [Theory]
    [InlineData(typeof(TipoEquipo), TipoEquipoConfiguracion.IndiceNombre, nameof(TipoEquipo.Nombre))]
    [InlineData(typeof(Ubicacion), UbicacionConfiguracion.IndiceNombre, nameof(Ubicacion.Nombre))]
    [InlineData(typeof(Proveedor), ProveedorConfiguracion.IndiceNit, nameof(Proveedor.Nit))]
    public void Catalogos_IndicesUnicosDelScript01(Type entidad, string indice, string propiedad)
    {
        var tipo = Modelo().FindEntityType(entidad)!;

        var unico = Assert.Single(tipo.GetIndexes(), i => i.GetDatabaseName() == indice);

        Assert.True(unico.IsUnique);
        Assert.Equal(["TenantId", propiedad], unico.Properties.Select(p => p.Name));
    }
}
