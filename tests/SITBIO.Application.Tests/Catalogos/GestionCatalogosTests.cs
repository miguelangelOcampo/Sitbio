using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Catalogos;

/// <summary>Gestión de catálogos maestros (HU-025): permisos por rol, duplicados, inactivación y bitácora.</summary>
public class GestionCatalogosTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly IRepositorioCatalogos _repositorio = Substitute.For<IRepositorioCatalogos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly GestionCatalogos _caso;

    public GestionCatalogosTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_usuario);
        _contexto.Rol.Returns(CodigosRol.IngenieroBiomedico);
        _caso = new GestionCatalogos(_repositorio, _auditoria, _contexto);
    }

    private void ComoRol(string rol) => _contexto.Rol.Returns(rol);

    private T Existente<T>(string tipo, T elemento)
        where T : IElementoCatalogo
    {
        _repositorio.ObtenerAsync(tipo, elemento.Id, Arg.Any<CancellationToken>()).Returns(elemento);
        return elemento;
    }

    private Task Auditado(string accion, string entidad, Func<IReadOnlyDictionary<string, string>?, bool> detalle) =>
        _auditoria.Received(1).RegistrarAsync(_usuario, accion, entidad, Arg.Any<string?>(), Arg.Is<IReadOnlyDictionary<string, string>?>(d => detalle(d)), Arg.Any<CancellationToken>());

    // ---------- CA-HU-025-01: crear en cada catálogo ----------

    [Theory]
    [InlineData(TiposCatalogo.TiposEquipo, "tipo_equipo")]
    [InlineData(TiposCatalogo.Ubicaciones, "ubicacion")]
    [InlineData(TiposCatalogo.Proveedores, "proveedor")]
    [InlineData(TiposCatalogo.TiposFalla, "tipo_falla")]
    public async Task CA_HU_025_01_Crear_GuardaActivoYQuedaEnLaBitacora(string tipo, string entidad)
    {
        var datos = new DatosCatalogo(" Nuevo valor ", Nit: "900.123.456-7", TipoProveedor: "Distribuidor");

        var creado = await _caso.CrearAsync(tipo, datos, CancellationToken.None);

        Assert.Equal(("Nuevo valor", true), (creado.Nombre, creado.Activo));
        _repositorio.Received(1).Agregar(Arg.Is<IElementoCatalogo>(e => e.Nombre == "Nuevo valor" && e.EstadoActivo));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await Auditado(AccionesAuditoria.Crear, entidad, d => d!["nombre"] == "Nuevo valor");
    }

    [Fact]
    public async Task TipoDeEquipo_SinDatosRegulatorios_RequiereCalibracionCada12MesesYClasePorDefinir()
    {
        var creado = await _caso.CrearAsync(TiposCatalogo.TiposEquipo, new DatosCatalogo("Tensiómetro"), CancellationToken.None);

        Assert.Equal(((string?)null, (bool?)true, (int?)12), (creado.ClaseRiesgo, creado.RequiereCalibracion, creado.PeriodicidadCalibracionMeses));
    }

    [Fact]
    public async Task TipoDeEquipo_SinCalibracion_QuedaSinPeriodicidad()
    {
        var creado = await _caso.CrearAsync(
            TiposCatalogo.TiposEquipo, new DatosCatalogo("Estetoscopio", ClaseRiesgo: "I", RequiereCalibracion: false), CancellationToken.None);

        Assert.Equal(("I", (bool?)false, (int?)null), (creado.ClaseRiesgo, creado.RequiereCalibracion, creado.PeriodicidadCalibracionMeses));
    }

    [Theory]
    [InlineData(TiposCatalogo.TiposFalla, "", null, null, "nombre")]
    [InlineData(TiposCatalogo.Proveedores, "Equimédica", null, "Distribuidor", "nit")]
    [InlineData(TiposCatalogo.Proveedores, "Equimédica", "800.111.222-3", null, "tipoProveedor")]
    public async Task DatoInvalido_Responde400PorCampoSinGuardar(string tipo, string nombre, string? nit, string? tipoProveedor, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() =>
            _caso.CrearAsync(tipo, new DatosCatalogo(nombre, Nit: nit, TipoProveedor: tipoProveedor), CancellationToken.None));

        Assert.True(error.Errores.ContainsKey(campo));
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Theory]
    [InlineData("IV", null, "claseRiesgo")]
    [InlineData("IIa", 61, "periodicidadCalibracionMeses")]
    public async Task TipoDeEquipo_ClaseOPeriodicidadInvalida_SeMarcaConElCampoDelCuerpo(string clase, int? meses, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(
            TiposCatalogo.TiposEquipo, new DatosCatalogo("Monitor", ClaseRiesgo: clase, PeriodicidadCalibracionMeses: meses), CancellationToken.None));

        Assert.Equal([campo], error.Errores.Keys);
    }

    [Fact]
    public async Task Proveedor_CorreoInvalido_Responde400EnCorreo()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.CrearAsync(
            TiposCatalogo.Proveedores, new DatosCatalogo("Equimédica", Nit: "800.111.222-3", TipoProveedor: "Distribuidor", Correo: "soporte"), CancellationToken.None));

        Assert.Equal(["correo"], error.Errores.Keys);
    }

    // ---------- Duplicados (409 CAT-001) ----------

    [Fact]
    public async Task NombreRepetido_Responde409SinGuardar()
    {
        _repositorio.NombreEnUsoAsync(TiposCatalogo.TiposFalla, "No enciende", null, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() =>
            _caso.CrearAsync(TiposCatalogo.TiposFalla, new DatosCatalogo("No enciende"), CancellationToken.None));

        Assert.Equal(ErroresCatalogo.CodigoDuplicado, error.Codigo);
        Assert.Contains("tipos de falla", error.Message);
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task NitDeProveedorRepetido_Responde409()
    {
        _repositorio.NitProveedorEnUsoAsync("800.111.222-3", null, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => _caso.CrearAsync(
            TiposCatalogo.Proveedores, new DatosCatalogo("Equimédica", Nit: "800.111.222-3", TipoProveedor: "Distribuidor"), CancellationToken.None));

        Assert.Equal(ErroresCatalogo.CodigoDuplicado, error.Codigo);
        Assert.Contains("NIT 800.111.222-3", error.Message);
    }

    [Fact]
    public async Task Editar_ComparaElNombreExceptoConElMismoElemento()
    {
        var ubicacion = Existente(TiposCatalogo.Ubicaciones, new Ubicacion(Tenant, "Urgencias"));

        await _caso.ActualizarAsync(TiposCatalogo.Ubicaciones, ubicacion.Id, new DatosCatalogo("URGENCIAS"), CancellationToken.None);

        await _repositorio.Received(1).NombreEnUsoAsync(TiposCatalogo.Ubicaciones, "URGENCIAS", ubicacion.Id, Arg.Any<CancellationToken>());
    }

    // ---------- CA-HU-025-03: permisos por rol ----------

    [Fact]
    public async Task CA_HU_025_03_Administrador_GestionaUbicaciones()
    {
        ComoRol(CodigosRol.AdministradorSistema);

        var creada = await _caso.CrearAsync(TiposCatalogo.Ubicaciones, new DatosCatalogo("Rack R-07", "Cuarto de cableado"), CancellationToken.None);

        Assert.Equal("Cuarto de cableado", creada.Descripcion);
    }

    [Theory]
    [InlineData(TiposCatalogo.TiposEquipo, "tipos de equipo")]
    [InlineData(TiposCatalogo.Proveedores, "proveedores")]
    [InlineData(TiposCatalogo.TiposFalla, "tipos de falla")]
    public async Task CA_HU_025_03_Administrador_SoloConsultaLosCatalogosTecnicos(string tipo, string nombre)
    {
        ComoRol(CodigosRol.AdministradorSistema);
        var id = Guid.NewGuid();

        var alCrear = await Assert.ThrowsAsync<ErrorPermiso>(() => _caso.CrearAsync(tipo, new DatosCatalogo("Valor"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorPermiso>(() => _caso.ActualizarAsync(tipo, id, new DatosCatalogo("Valor"), CancellationToken.None));

        Assert.Contains(nombre, alCrear.Message);
        await _repositorio.DidNotReceiveWithAnyArgs().ObtenerAsync(default!, default, default);
        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
    }

    [Fact]
    public async Task OtroRol_NoGestionaNingunCatalogo()
    {
        ComoRol(CodigosRol.TecnicoMantenimiento);

        await Assert.ThrowsAsync<ErrorPermiso>(() => _caso.CrearAsync(TiposCatalogo.Ubicaciones, new DatosCatalogo("Urgencias"), CancellationToken.None));
    }

    // ---------- CA-HU-025-02: inactivar y activar ----------

    [Fact]
    public async Task CA_HU_025_02_Inactivar_ConservaElNombreYQuedaEnLaBitacora()
    {
        var falla = Existente(TiposCatalogo.TiposFalla, new TipoFalla(Tenant, "Eléctrica"));

        var editado = await _caso.ActualizarAsync(TiposCatalogo.TiposFalla, falla.Id, new DatosCatalogo("Eléctrica", Activo: false), CancellationToken.None);

        Assert.Equal(("Eléctrica", false), (editado.Nombre, editado.Activo));
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await Auditado(AccionesAuditoria.Actualizar, "tipo_falla", d => d!["estado"] == "inactivo" && d["campos"] == "estado_activo");
    }

    [Fact]
    public async Task Activar_UnElementoInactivo()
    {
        var ubicacion = new Ubicacion(Tenant, "Bodega");
        ubicacion.Inactivar();
        Existente(TiposCatalogo.Ubicaciones, ubicacion);

        var editado = await _caso.ActualizarAsync(TiposCatalogo.Ubicaciones, ubicacion.Id, new DatosCatalogo("Bodega", Activo: true), CancellationToken.None);

        Assert.True(editado.Activo);
        await Auditado(AccionesAuditoria.Actualizar, "ubicacion", d => d!["estado"] == "activo");
    }

    [Fact]
    public async Task SinActivo_ConservaElEstado()
    {
        var ubicacion = new Ubicacion(Tenant, "Bodega");
        ubicacion.Inactivar();
        Existente(TiposCatalogo.Ubicaciones, ubicacion);

        var editado = await _caso.ActualizarAsync(TiposCatalogo.Ubicaciones, ubicacion.Id, new DatosCatalogo("Bodega central"), CancellationToken.None);

        Assert.False(editado.Activo);
        await Auditado(AccionesAuditoria.Actualizar, "ubicacion", d => d!["campos"] == "nombre" && !d.ContainsKey("estado"));
    }

    [Fact]
    public async Task SinCambios_NoGuardaNiAudita()
    {
        var falla = Existente(TiposCatalogo.TiposFalla, new TipoFalla(Tenant, "Mecánica"));

        await _caso.ActualizarAsync(TiposCatalogo.TiposFalla, falla.Id, new DatosCatalogo(" Mecánica ", Activo: true), CancellationToken.None);

        await _repositorio.DidNotReceiveWithAnyArgs().GuardarAsync(default);
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!);
    }

    // ---------- Bitácora sin datos personales; cambios regulatorios del tipo ----------

    [Fact]
    public async Task Proveedor_CambioDeContacto_SeAuditaSinSuValor()
    {
        var proveedor = Existente(
            TiposCatalogo.Proveedores,
            new Proveedor(Tenant, "Equimédica", "800.111.222-3", "Distribuidor", new ContactoProveedor("Ana Ruiz", "ana@equimedica.co", "300 111 2233")));

        var editado = await _caso.ActualizarAsync(
            TiposCatalogo.Proveedores,
            proveedor.Id,
            new DatosCatalogo("Equimédica", Nit: "800.111.222-3", TipoProveedor: "Distribuidor", NombreContacto: "Luis Mora", Correo: "luis@equimedica.co"),
            CancellationToken.None);

        Assert.Equal(("Luis Mora", "luis@equimedica.co", (string?)null), (editado.NombreContacto, editado.Correo, editado.Telefono));
        await Auditado(
            AccionesAuditoria.Actualizar,
            "proveedor",
            d => d!["campos"] == "contacto" && !d.Values.Any(v => v.Contains("Luis", StringComparison.Ordinal) || v.Contains('@', StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TipoDeEquipo_CambiarValoresPorDefecto_QuedaEnLaBitacora()
    {
        var tipo = new TipoEquipo(Tenant, "Monitor");
        tipo.DefinirValoresRegulatorios("IIa", true, 12);
        Existente(TiposCatalogo.TiposEquipo, tipo);

        var editado = await _caso.ActualizarAsync(
            TiposCatalogo.TiposEquipo, tipo.Id, new DatosCatalogo("Monitor", ClaseRiesgo: "IIb", PeriodicidadCalibracionMeses: 6), CancellationToken.None);

        Assert.Equal(("IIb", (int?)6), (editado.ClaseRiesgo, editado.PeriodicidadCalibracionMeses));
        await Auditado(AccionesAuditoria.Actualizar, "tipo_equipo", d => d!["campos"] == "clase_riesgo_defecto,periodicidad_calibracion_meses");
    }

    [Fact]
    public async Task TipoDeEquipo_DejarDeCalibrar_BorraLaPeriodicidad()
    {
        var tipo = Existente(TiposCatalogo.TiposEquipo, new TipoEquipo(Tenant, "Estetoscopio"));

        var editado = await _caso.ActualizarAsync(
            TiposCatalogo.TiposEquipo, tipo.Id, new DatosCatalogo("Estetoscopio", RequiereCalibracion: false), CancellationToken.None);

        Assert.Equal(((bool?)false, (int?)null), (editado.RequiereCalibracion, editado.PeriodicidadCalibracionMeses));
    }

    // ---------- 404 ----------

    [Fact]
    public async Task ElementoInexistenteODeOtraInstitucion_Responde404()
    {
        _repositorio.ObtenerAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IElementoCatalogo?)null);

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            _caso.ActualizarAsync(TiposCatalogo.Ubicaciones, Guid.NewGuid(), new DatosCatalogo("Urgencias"), CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(TiposCatalogo.Ubicaciones, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CatalogoDesconocido_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.CrearAsync("campos-dinamicos", new DatosCatalogo("x"), CancellationToken.None));
    }

    [Fact]
    public async Task Obtener_ProveedorTraeSuContacto()
    {
        var proveedor = Existente(
            TiposCatalogo.Proveedores,
            new Proveedor(Tenant, "CalibraLab", "901.555.222-1", "Laboratorio de calibración", new ContactoProveedor("Mesa de ayuda", "ayuda@calibralab.co", null)));

        var detalle = await _caso.ObtenerAsync("Proveedores", proveedor.Id, CancellationToken.None);

        Assert.Equal(("901.555.222-1", "Mesa de ayuda", "ayuda@calibralab.co"), (detalle.Nit, detalle.NombreContacto, detalle.Correo));
    }
}
