using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-025 (DOC-04, CP-028) contra la API y PostgreSQL reales: el
/// Ingeniero Biomédico (AC-02) y el Administrador del Sistema (AC-01) inician sesión de verdad y el
/// resultado se comprueba con la API de equipos y en la base. Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu025Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/catalogos";
    private const string RutaEquipos = "/api/v1/equipos";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);

    private static readonly object Monitor = new
    {
        nombre = "Monitor de signos vitales",
        descripcion = "Monitor multiparamétrico de cabecera",
        claseRiesgo = ClasesRiesgo.IIb,
        requiereCalibracion = true,
        periodicidadCalibracionMeses = 12,
    };

    // ---------- CA-HU-025-01 ----------

    [FactIntegracion]
    public async Task CA_HU_025_01_ElementoNuevo_ApareceEnElCatalogoYSeUsaEnElFormularioDeEquipos()
    {
        var (tenant, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var (_, otraInstitucion) = await InstitucionCon(CodigosRol.IngenieroBiomedico);

        var tipo = await Crear(ingeniero, TiposCatalogo.TiposEquipo, Monitor);
        var ubicacion = await Crear(ingeniero, TiposCatalogo.Ubicaciones, new { nombre = "Quirófano 3" });
        var proveedor = await Crear(ingeniero, TiposCatalogo.Proveedores, new { nombre = "Biomédica Andina", nit = "900123456-7", tipoProveedor = "Servicio técnico" });
        var falla = await Crear(ingeniero, TiposCatalogo.TiposFalla, new { nombre = "Alarma sin causa aparente" });

        foreach (var (catalogo, id) in new[]
        {
            (TiposCatalogo.TiposEquipo, tipo),
            (TiposCatalogo.Ubicaciones, ubicacion),
            (TiposCatalogo.Proveedores, proveedor),
            (TiposCatalogo.TiposFalla, falla),
        })
        {
            var elemento = Assert.Single(await Listar(ingeniero, catalogo), e => Id(e) == id);
            Assert.True(elemento.GetProperty("activo").GetBoolean());

            // Solo en los formularios de su institución (RN-008).
            Assert.DoesNotContain(await Listar(otraInstitucion, catalogo), e => Id(e) == id);
        }

        var tipoListado = (await Listar(ingeniero, TiposCatalogo.TiposEquipo)).Single(e => Id(e) == tipo);
        Assert.Equal(ClasesRiesgo.IIb, tipoListado.GetProperty("claseRiesgo").GetString());
        Assert.Equal(12, tipoListado.GetProperty("periodicidadCalibracionMeses").GetInt32());

        var registro = await ingeniero.PostAsJsonAsync(RutaEquipos, Equipo(tipo, ubicacion, proveedor, "CAT-0001"));

        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var equipo = await api.LeerEquipoAsync(Id(await Cuerpo(registro)));
        Assert.Equal(tenant, equipo.TenantId);
        Assert.Equal(tipo, equipo.TipoEquipoId);
        Assert.Equal(ubicacion, equipo.UbicacionId);
    }

    [FactIntegracion]
    public async Task CA_HU_025_01_NombreRepetidoEnLaInstitucion_Responde409SinDuplicar()
    {
        var (_, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        await Crear(ingeniero, TiposCatalogo.Ubicaciones, new { nombre = "UCI Adultos" });

        var respuesta = await ingeniero.PostAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}", new { nombre = "uci adultos" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal(ErroresCatalogo.CodigoDuplicado, (await Cuerpo(respuesta)).GetProperty("codigo").GetString());
        Assert.Single(await Listar(ingeniero, TiposCatalogo.Ubicaciones));
    }

    // ---------- CA-HU-025-02 ----------

    [FactIntegracion]
    public async Task CA_HU_025_02_InactivarUnElementoUsado_LosRegistrosAnterioresConservanSuValor()
    {
        var (_, ingeniero) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var tipo = await Crear(ingeniero, TiposCatalogo.TiposEquipo, Monitor);
        var ubicacion = await Crear(ingeniero, TiposCatalogo.Ubicaciones, new { nombre = "Quirófano 3" });
        var datosEquipo = Equipo(tipo, ubicacion, null, "CAT-0100");
        var registro = await ingeniero.PostAsJsonAsync(RutaEquipos, datosEquipo);
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var equipoId = Id(await Cuerpo(registro));

        var inactivarUbicacion = await ingeniero.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}", new { nombre = "Quirófano 3", activo = false });
        var inactivarTipo = await ingeniero.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.TiposEquipo}/{tipo}", new
        {
            nombre = "Monitor de signos vitales",
            descripcion = "Monitor multiparamétrico de cabecera",
            claseRiesgo = ClasesRiesgo.IIb,
            requiereCalibracion = true,
            periodicidadCalibracionMeses = 12,
            activo = false,
        });

        Assert.Equal(HttpStatusCode.OK, inactivarUbicacion.StatusCode);
        Assert.False((await Cuerpo(inactivarUbicacion)).GetProperty("activo").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, inactivarTipo.StatusCode);

        // El equipo registrado antes conserva el tipo y la ubicación, con sus nombres.
        var hojaDeVida = await Cuerpo(await ingeniero.GetAsync($"{RutaEquipos}/{equipoId}"));
        Assert.Equal(tipo, Guid.Parse(hojaDeVida.GetProperty("tipoEquipoId").GetString()!));
        Assert.Equal("Monitor de signos vitales", hojaDeVida.GetProperty("tipoEquipo").GetString());
        Assert.Equal(ubicacion, Guid.Parse(hojaDeVida.GetProperty("ubicacionId").GetString()!));
        Assert.Equal("Quirófano 3", hojaDeVida.GetProperty("ubicacion").GetString());

        // No hay borrado: el elemento sigue en el catálogo, inactivo.
        Assert.False((await Listar(ingeniero, TiposCatalogo.Ubicaciones)).Single(e => Id(e) == ubicacion).GetProperty("activo").GetBoolean());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await ingeniero.DeleteAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}")).StatusCode);

        // El equipo se sigue editando con sus valores; un equipo nuevo ya no puede usarlos.
        datosEquipo["modelo"] = "VN-600";
        Assert.Equal(HttpStatusCode.OK, (await ingeniero.PutAsJsonAsync($"{RutaEquipos}/{equipoId}", datosEquipo)).StatusCode);
        var nuevo = await ingeniero.PostAsJsonAsync(RutaEquipos, Equipo(tipo, ubicacion, null, "CAT-0101"));
        Assert.Equal(HttpStatusCode.BadRequest, nuevo.StatusCode);
        var errores = (await Cuerpo(nuevo)).GetProperty("errors");
        Assert.True(errores.TryGetProperty("tipoEquipoId", out _));
        Assert.True(errores.TryGetProperty("ubicacionId", out _));
    }

    // ---------- CA-HU-025-03 ----------

    [FactIntegracion]
    public async Task CA_HU_025_03_ElAdministradorGestionaUbicacionesYSoloConsultaLosCatalogosTecnicos()
    {
        var (tenant, administrador) = await InstitucionCon(CodigosRol.AdministradorSistema);
        var ingeniero = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var tipo = await Crear(ingeniero, TiposCatalogo.TiposEquipo, Monitor);

        // Áreas y ubicaciones, también con la nomenclatura propia de TI.
        var ubicacion = await Crear(administrador, TiposCatalogo.Ubicaciones, new { nombre = "Rack 2", descripcion = "Cuarto técnico, punto de red R2-14" });
        var edicion = await administrador.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}", new { nombre = "Rack 2 - Piso 3", descripcion = "Cuarto técnico, punto de red R2-14" });
        var inactivar = await administrador.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}", new { nombre = "Rack 2 - Piso 3", activo = false });

        Assert.Equal(HttpStatusCode.OK, edicion.StatusCode);
        Assert.Equal("Rack 2 - Piso 3", (await Cuerpo(edicion)).GetProperty("nombre").GetString());
        Assert.Equal(HttpStatusCode.OK, inactivar.StatusCode);
        Assert.False((await Cuerpo(inactivar)).GetProperty("activo").GetBoolean());

        // Los catálogos técnicos: los consulta, pero no los crea ni los edita.
        foreach (var catalogo in new[] { TiposCatalogo.TiposEquipo, TiposCatalogo.Proveedores, TiposCatalogo.TiposFalla })
        {
            Assert.Equal(HttpStatusCode.OK, (await administrador.GetAsync($"{Ruta}/{catalogo}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await administrador.PostAsJsonAsync($"{Ruta}/{catalogo}", new { nombre = "Intento", nit = "800111222-3", tipoProveedor = "Distribuidor" })).StatusCode);
        }

        var editarTipo = await administrador.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.TiposEquipo}/{tipo}", new { nombre = "Cambiado por AC-01", claseRiesgo = ClasesRiesgo.I, requiereCalibracion = false });

        Assert.Equal(HttpStatusCode.Forbidden, editarTipo.StatusCode);
        var tipoGuardado = (await Listar(ingeniero, TiposCatalogo.TiposEquipo)).Single(e => Id(e) == tipo);
        Assert.Equal("Monitor de signos vitales", tipoGuardado.GetProperty("nombre").GetString());
        Assert.Empty(await Listar(ingeniero, TiposCatalogo.TiposFalla));
        Assert.Empty(await Listar(ingeniero, TiposCatalogo.Proveedores));
    }

    // ---------- Permisos (RNF-003) y aislamiento (RN-008) ----------

    [FactIntegracion]
    public async Task Permisos_OtrosRolesReciben403YSinToken401()
    {
        var (tenant, _) = await InstitucionCon(CodigosRol.IngenieroBiomedico);

        foreach (var rol in new[] { CodigosRol.TecnicoMantenimiento, CodigosRol.GerenteBiomedico, CodigosRol.PersonalClinico })
        {
            var cliente = await SesionDe(tenant, rol);

            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}", new { nombre = $"Intento {rol}" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}")).StatusCode);
    }

    [FactIntegracion]
    public async Task Aislamiento_OtraInstitucionNoVeNiEditaElElemento()
    {
        var (_, propia) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var (_, ajena) = await InstitucionCon(CodigosRol.IngenieroBiomedico);
        var ubicacion = await Crear(propia, TiposCatalogo.Ubicaciones, new { nombre = "Sala de partos" });

        Assert.Equal(HttpStatusCode.NotFound, (await ajena.GetAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ajena.PutAsJsonAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}", new { nombre = "Robada", activo = false })).StatusCode);

        var detalle = await Cuerpo(await propia.GetAsync($"{Ruta}/{TiposCatalogo.Ubicaciones}/{ubicacion}"));
        Assert.Equal("Sala de partos", detalle.GetProperty("nombre").GetString());
        Assert.True(detalle.GetProperty("activo").GetBoolean());
    }

    private async Task<(Guid Tenant, HttpClient Cliente)> InstitucionCon(string codigoRol)
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        return (tenant, await SesionDe(tenant, codigoRol));
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static async Task<Guid> Crear(HttpClient cliente, string catalogo, object datos)
    {
        var respuesta = await cliente.PostAsJsonAsync($"{Ruta}/{catalogo}", datos);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var id = Id(await Cuerpo(respuesta));
        Assert.EndsWith($"/catalogos/{catalogo}/{id}", respuesta.Headers.Location!.ToString());
        return id;
    }

    private static async Task<List<JsonElement>> Listar(HttpClient cliente, string catalogo)
    {
        var respuesta = await cliente.GetAsync($"{Ruta}/{catalogo}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await Cuerpo(respuesta)).EnumerateArray().ToList();
    }

    /// <summary>Datos completos de un monitor (RN-010) con los elementos del catálogo indicados.</summary>
    private static Dictionary<string, object?> Equipo(Guid tipo, Guid ubicacion, Guid? proveedor, string placa) => new()
    {
        ["tipoEquipoId"] = tipo,
        ["ubicacionId"] = ubicacion,
        ["proveedorId"] = proveedor,
        ["numeroSerie"] = $"SN-{placa}",
        ["placaInventario"] = placa,
        ["marca"] = "VitalNorte",
        ["modelo"] = "VN-500",
        ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
        ["registroInvima"] = "INVIMA 2020DM-0004567",
        ["venceRegistro"] = Hoy.AddYears(2),
        ["claseRiesgo"] = ClasesRiesgo.IIb,
        ["requiereCalibracion"] = true,
        ["periodicidadCalibracionMeses"] = 12,
    };

    private static Guid Id(JsonElement elemento) => Guid.Parse(elemento.GetProperty("id").GetString()!);

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
