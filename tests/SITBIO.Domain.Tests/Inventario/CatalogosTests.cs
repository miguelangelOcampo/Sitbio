using Sitbio.Domain.Inventario;

namespace Sitbio.Domain.Tests.Inventario;

/// <summary>Elementos de los catálogos maestros (HU-025): validación, edición e inactivación.</summary>
public class CatalogosTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    public static TheoryData<IElementoCatalogo> Elementos() =>
    [
        new TipoEquipo(Tenant, "Tensiómetro"),
        new Ubicacion(Tenant, "Urgencias"),
        new Proveedor(Tenant, "Biomédica Andina", "900.123.456-7", "Distribuidor"),
        new TipoFalla(Tenant, "No enciende"),
    ];

    // ---------- CA-HU-025-01 y CA-HU-025-02: nace activo; se inactiva sin borrarse ----------

    [Theory]
    [MemberData(nameof(Elementos))]
    public void Elemento_NaceActivoYSeInactivaConservandoSuNombre(IElementoCatalogo elemento)
    {
        Assert.True(elemento.EstadoActivo);
        var nombre = elemento.Nombre;

        elemento.Inactivar();
        Assert.False(elemento.EstadoActivo);
        Assert.Equal(nombre, elemento.Nombre);

        elemento.Activar();
        Assert.True(elemento.EstadoActivo);
    }

    // ---------- Ubicación ----------

    [Fact]
    public void Ubicacion_QuitaEspaciosYAceptaDescripcionOpcional()
    {
        var ubicacion = new Ubicacion(Tenant, "  UCI adultos ", "   ");

        Assert.Equal("UCI adultos", ubicacion.Nombre);
        Assert.Null(ubicacion.Descripcion);

        ubicacion.Actualizar("UCI adultos", " Piso 3, rack R-07 ");
        Assert.Equal("Piso 3, rack R-07", ubicacion.Descripcion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ubicacion_SinNombre_SeRechaza(string nombre)
    {
        var error = Assert.Throws<ArgumentException>(() => new Ubicacion(Tenant, nombre));

        Assert.Equal("nombre", error.ParamName);
    }

    [Fact]
    public void Ubicacion_NombreMasLargoQueLaColumna_SeRechaza()
    {
        Assert.Throws<ArgumentException>(() => new Ubicacion(Tenant, new string('a', Ubicacion.LongitudMaximaNombre + 1)));
        Assert.Equal(Ubicacion.LongitudMaximaNombre, new Ubicacion(Tenant, new string('a', Ubicacion.LongitudMaximaNombre)).Nombre.Length);
    }

    // ---------- Proveedor ----------

    [Fact]
    public void Proveedor_GuardaContactoYCorreoEnMinusculas()
    {
        var proveedor = new Proveedor(
            Tenant, " Metrología Valle ", " 901.555.222-1 ", "Laboratorio de calibración",
            new ContactoProveedor(" Mesa de ayuda ", " Soporte@MetrologiaValle.co ", " 604 444 5555 "));

        Assert.Equal(("Metrología Valle", "901.555.222-1", "Laboratorio de calibración"), (proveedor.Nombre, proveedor.Nit, proveedor.TipoProveedor));
        Assert.Equal(("Mesa de ayuda", "soporte@metrologiavalle.co", "604 444 5555"), (proveedor.NombreContacto, proveedor.Correo, proveedor.Telefono));
    }

    [Fact]
    public void Proveedor_SinContacto_QuedaVacio()
    {
        var proveedor = new Proveedor(Tenant, "Equimédica", "800.111.222-3", "Servicio técnico");

        Assert.Null(proveedor.NombreContacto);
        Assert.Null(proveedor.Correo);
        Assert.Null(proveedor.Telefono);
    }

    [Theory]
    [InlineData("", "800.111.222-3", "Distribuidor", "nombre")]
    [InlineData("Equimédica", " ", "Distribuidor", "nit")]
    [InlineData("Equimédica", "800.111.222-3", "", "tipoProveedor")]
    [InlineData("Equimédica", "800.111.222-3-0000000000", "Distribuidor", "nit")]
    public void Proveedor_DatoObligatorioInvalido_SeRechazaConSuCampo(string nombre, string nit, string tipo, string campo)
    {
        var error = Assert.Throws<ArgumentException>(() => new Proveedor(Tenant, nombre, nit, tipo));

        Assert.Equal(campo, error.ParamName);
    }

    [Theory]
    [InlineData("soporte")]
    [InlineData("@proveedor.com")]
    [InlineData("soporte@")]
    [InlineData("so porte@proveedor.com")]
    public void Proveedor_CorreoInvalido_SeRechaza(string correo)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new Proveedor(Tenant, "Equimédica", "800.111.222-3", "Distribuidor", new ContactoProveedor(null, correo, null)));

        Assert.Equal("correo", error.ParamName);
    }

    [Fact]
    public void Proveedor_Actualizar_ReemplazaTodosLosDatos()
    {
        var proveedor = new Proveedor(Tenant, "Equimédica", "800.111.222-3", "Distribuidor", new ContactoProveedor("Ana", "ana@equimedica.co", "300"));

        proveedor.Actualizar("Equimédica Ltda.", "800.111.222-3", "Servicio técnico", null);

        Assert.Equal(("Equimédica Ltda.", "Servicio técnico"), (proveedor.Nombre, proveedor.TipoProveedor));
        Assert.Null(proveedor.Correo);
    }

    // ---------- Tipo de falla ----------

    [Fact]
    public void TipoFalla_LargoDeTiqueteTipoFalla()
    {
        Assert.Equal("Alarma o mensaje de error", new TipoFalla(Tenant, " Alarma o mensaje de error ").Nombre);
        Assert.Throws<ArgumentException>(() => new TipoFalla(Tenant, new string('x', TipoFalla.LongitudMaximaNombre + 1)));
    }

    [Fact]
    public void TipoFalla_Renombrar_ValidaElNombre()
    {
        var tipo = new TipoFalla(Tenant, "Electrica");

        tipo.Renombrar("Eléctrica");

        Assert.Equal("Eléctrica", tipo.Nombre);
        Assert.Throws<ArgumentException>(() => tipo.Renombrar(" "));
    }

    // ---------- Tipo de equipo ----------

    [Fact]
    public void TipoEquipo_Actualizar_CambiaNombreYDescripcionSinTocarLaCalibracion()
    {
        var tipo = new TipoEquipo(Tenant, "Monitor");
        tipo.DefinirValoresRegulatorios(ClasesRiesgo.IIb, true, 6);

        tipo.Actualizar("Monitor multiparámetro", "Signos vitales");

        Assert.Equal(("Monitor multiparámetro", "Signos vitales"), (tipo.Nombre, tipo.Descripcion));
        Assert.Equal((true, (short?)6), (tipo.RequiereCalibracion, tipo.PeriodicidadCalibracionMeses));
    }

    [Fact]
    public void Elemento_SinInstitucion_SeRechaza()
    {
        Assert.Throws<ArgumentException>(() => new TipoFalla(Guid.Empty, "No enciende"));
    }
}
