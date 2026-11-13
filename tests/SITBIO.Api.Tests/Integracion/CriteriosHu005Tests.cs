using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-005 (CP-008) contra la API, PostgreSQL y MongoDB reales: el Técnico
/// inicia y pausa su orden, el tiquete pasa a «en ejecución» y la intervención se guarda en
/// <c>intervenciones</c> con los <c>camposDinamicos</c> de la versión activa, enlazada a la orden. Cada
/// prueba usa su propia institución.
/// </summary>
public class CriteriosHu005Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/ordenes";
    private const string RutaTiquetes = "/api/v1/tiquetes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly object TasaDeGoteo = new { etiqueta = "Tasa de goteo", tipoDato = TiposDatoCampo.NumeroDecimal, obligatorio = true, unidad = "ml/h" };
    private static readonly object Oclusion = new { etiqueta = "Prueba de oclusión", tipoDato = TiposDatoCampo.Seleccion, obligatorio = true, opciones = new[] { "Pasa", "No pasa" } };

    private sealed record Institucion(
        Guid Tenant,
        HttpClient Ingeniero,
        HttpClient Clinico,
        HttpClient Tecnico,
        Guid TecnicoId,
        Guid TipoEquipo,
        Guid Equipo);

    // ---------- Iniciar y pausar ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_005_01_ElTecnicoIniciaYPausaSuOrden_YElTiquetePasaAEnEjecucion()
    {
        var i = await InstitucionConEquipo();
        var (orden, codigo) = await OrdenAsignada(i);

        var iniciada = await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, iniciada.StatusCode);
        var cuerpo = await Cuerpo(iniciada);
        Assert.Equal(EstadosOrden.EnEjecucion, Texto(cuerpo, "estado"));
        var inicio = cuerpo.GetProperty("fechaInicio").GetDateTimeOffset();

        // El Personal Clínico ve su tiquete «en ejecución», con el cambio a nombre del técnico.
        Assert.Equal(EstadosTiquete.EnEjecucion, Texto(await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}")), "estado"));
        var ultimo = (await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}/historial"))).EnumerateArray().Last();
        Assert.Equal((EstadosTiquete.Asignado, EstadosTiquete.EnEjecucion, i.TecnicoId), (Texto(ultimo, "estadoAnterior"), Texto(ultimo, "nuevoEstado"), ultimo.GetProperty("usuarioId").GetGuid()));

        // Pausar y reanudar no vuelven a tocar el tiquete ni la fecha de inicio.
        Assert.Equal(EstadosOrden.Pausada, Texto(await Cuerpo(await Estado(i.Tecnico, orden, AccionesOrden.Pausar)), "estado"));
        var reanudada = await Cuerpo(await Estado(i.Tecnico, orden, AccionesOrden.Iniciar));
        Assert.Equal((EstadosOrden.EnEjecucion, inicio), (Texto(reanudada, "estado"), reanudada.GetProperty("fechaInicio").GetDateTimeOffset()));
        var historial = (await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}/historial"))).EnumerateArray().ToList();
        Assert.Single(historial, h => Texto(h, "nuevoEstado") == EstadosTiquete.EnEjecucion);

        // Una transición fuera de orden se rechaza con su código.
        await Estado(i.Tecnico, orden, AccionesOrden.Pausar);
        var otraPausa = await Estado(i.Tecnico, orden, AccionesOrden.Pausar);
        Assert.Equal(HttpStatusCode.Conflict, otraPausa.StatusCode);
        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, Texto(await Cuerpo(otraPausa), "codigo"));

        // Cada cambio queda en la bitácora a nombre del técnico.
        var eventos = (await api.LeerBitacoraAsync(i.Tenant))
            .Where(e => e["entidad"].AsString == GestionOrdenes.Entidad && e["accion"].AsString == AccionesAuditoria.Actualizar).ToList();
        Assert.Equal(4, eventos.Count);
        Assert.All(eventos, e => Assert.Equal((orden.ToString(), i.TecnicoId.ToString()), (e["entidad_id"].AsString, e["usuario_id"].AsString)));
    }

    // ---------- Intervención con campos dinámicos ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_005_02_LaIntervencionSeGuardaEnMongoConLosCamposDeLaVersionActiva_YQuedaEnlazada()
    {
        var i = await InstitucionConEquipo();
        await Publicar(i, TasaDeGoteo);
        await Publicar(i, TasaDeGoteo, Oclusion);
        var (orden, _) = await OrdenAsignada(i);

        // Sin iniciar la orden no se registra.
        var sinIniciar = await Registrar(i.Tecnico, orden, Hoja(25.5, "pasa"));
        Assert.Equal(HttpStatusCode.Conflict, sinIniciar.StatusCode);
        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, Texto(await Cuerpo(sinIniciar), "codigo"));
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);

        var registrada = await Registrar(i.Tecnico, orden, Hoja(25.5, "pasa"));

        Assert.Equal(HttpStatusCode.OK, registrada.StatusCode);
        var dto = await Cuerpo(registrada);
        Assert.Equal((2, 70000m), (dto.GetProperty("versionCampos").GetInt32(), dto.GetProperty("costoTotal").GetDecimal()));
        Assert.Equal("Pasa", Texto(dto.GetProperty("camposDinamicos"), "prueba_de_oclusion"));

        // El documento pasó el $jsonSchema del script 02 con los tipos y nombres de la versión activa.
        var documento = Assert.Single(await api.LeerIntervencionesAsync(i.Tenant));
        Assert.Equal((orden.ToString(), i.Equipo.ToString(), i.TecnicoId.ToString()), (documento["orden_id"].AsString, documento["equipo_id"].AsString, documento["tecnico_id"].AsString));
        Assert.Equal((TiposMantenimiento.Correctivo, EstadosSalidaEquipo.Operativo, false), (documento["tipo_mantenimiento"].AsString, documento["estado_equipo_salida"].AsString, documento["cerrada_firmada"].AsBoolean));
        Assert.Equal((BsonType.Double, 25.5), (documento["camposDinamicos"]["tasa_de_goteo"].BsonType, documento["camposDinamicos"]["tasa_de_goteo"].AsDouble));
        Assert.Equal(2, documento["version_campos"].AsInt32);

        // La orden queda enlazada y la intervención se consulta desde la orden.
        Assert.True((await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}/{orden}"))).GetProperty("tieneIntervencion").GetBoolean());
        var consultada = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{orden}/intervencion"));
        Assert.Equal(dto.GetProperty("id").GetGuid(), consultada.GetProperty("id").GetGuid());

        // Corregirla reemplaza el mismo documento.
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja(30, "No pasa"))).StatusCode);
        var corregido = Assert.Single(await api.LeerIntervencionesAsync(i.Tenant));
        Assert.Equal((documento["_id"].AsObjectId, 30d, "No pasa"), (corregido["_id"].AsObjectId, corregido["camposDinamicos"]["tasa_de_goteo"].AsDouble, corregido["camposDinamicos"]["prueba_de_oclusion"].AsString));

        // La bitácora registra la creación y la corrección con el ObjectId.
        var eventos = (await api.LeerBitacoraAsync(i.Tenant)).Where(e => e["entidad"].AsString == GestionEjecucion.EntidadIntervencion).ToList();
        Assert.Equal([AccionesAuditoria.Actualizar, AccionesAuditoria.Crear], eventos.Select(e => e["accion"].AsString));
        Assert.All(eventos, e => Assert.Equal(documento["_id"].AsObjectId.ToString(), e["entidad_id"].AsString));
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_005_02_CamposDinamicosInvalidos_SeRechazanJuntoAlCampoSinGuardarNada()
    {
        var i = await InstitucionConEquipo();
        await Publicar(i, TasaDeGoteo, Oclusion);
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);

        var respuesta = await Registrar(i.Tecnico, orden, new
        {
            diagnostico = "Sensor descalibrado",
            accionesRealizadas = "Calibración",
            camposDinamicos = new Dictionary<string, object> { ["tasa_de_goteo"] = "alta", ["presion"] = 120 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var errores = (await Cuerpo(respuesta)).GetProperty("errors");
        Assert.Equal(
            ["camposDinamicos.presion", "camposDinamicos.prueba_de_oclusion", "camposDinamicos.tasa_de_goteo"],
            errores.EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Empty(await api.LeerIntervencionesAsync(i.Tenant));
        Assert.False((await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}/{orden}"))).GetProperty("tieneIntervencion").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await i.Tecnico.GetAsync($"{Ruta}/{orden}/intervencion")).StatusCode);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task IntervencionFirmada_NoSeModifica_Rn004()
    {
        var i = await InstitucionConEquipo();
        await Publicar(i, TasaDeGoteo);
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja(25.5))).StatusCode);
        await api.FirmarIntervencionAsync(orden);

        var respuesta = await Registrar(i.Tecnico, orden, Hoja(99));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal(GestionEjecucion.CodigoFirmada, Texto(await Cuerpo(respuesta), "codigo"));
        Assert.Equal(25.5, Assert.Single(await api.LeerIntervencionesAsync(i.Tenant))["camposDinamicos"]["tasa_de_goteo"].AsDouble);
    }

    // ---------- Permisos y aislamiento ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task Permisos_SoloElTecnicoAsignadoEjecuta_YOtraInstitucionNoLaVe()
    {
        var i = await InstitucionConEquipo();
        await Publicar(i, TasaDeGoteo);
        var (orden, _) = await OrdenAsignada(i);
        var (otroTecnico, _) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var (tecnicoAjeno, _) = await SesionDe(api.TenantB, CodigosRol.TecnicoMantenimiento);

        // Otro técnico, de la misma institución o de otra, no ve la orden (404).
        foreach (var cliente in new[] { otroTecnico, tecnicoAjeno })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Estado(cliente, orden, AccionesOrden.Iniciar)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Registrar(cliente, orden, Hoja(25.5))).StatusCode);
        }

        // Quien no es técnico no inicia ni registra (403); el Personal Clínico tampoco consulta.
        Assert.Equal(HttpStatusCode.Forbidden, (await Estado(i.Ingeniero, orden, AccionesOrden.Iniciar)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(i.Ingeniero, orden, Hoja(25.5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Clinico.GetAsync($"{Ruta}/{orden}/intervencion")).StatusCode);
        Assert.Equal(EstadosOrden.Asignada, Texto(await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}/{orden}")), "estado"));

        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja(25.5))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otroTecnico.GetAsync($"{Ruta}/{orden}/intervencion")).StatusCode);
        Assert.Empty(await api.LeerIntervencionesAsync(api.TenantB));
    }

    // ---------- Apoyo ----------

    private static object Hoja(double goteo, string? oclusion = null)
    {
        var campos = new Dictionary<string, object> { ["tasa_de_goteo"] = goteo };
        if (oclusion is not null)
        {
            campos["prueba_de_oclusion"] = oclusion;
        }

        return new
        {
            diagnostico = "Sensor de presión descalibrado",
            accionesRealizadas = "Se calibró el sensor y se verificó con el analizador",
            repuestos = new[] { new { nombre = "Sensor de presión", cantidad = 2, costoUnitario = 15000 } },
            costoManoObra = 40000,
            tiempoAtencionMin = 90,
            estadoEquipoSalida = EstadosSalidaEquipo.Operativo,
            camposDinamicos = campos,
        };
    }

    private static Task<HttpResponseMessage> Estado(HttpClient cliente, Guid orden, string accion) =>
        cliente.PatchAsJsonAsync($"{Ruta}/{orden}/estado", new { accion });

    private static Task<HttpResponseMessage> Registrar(HttpClient cliente, Guid orden, object hoja) =>
        cliente.PutAsJsonAsync($"{Ruta}/{orden}/intervencion", hoja);

    private static async Task Publicar(Institucion i, params object[] campos) =>
        Assert.Equal(HttpStatusCode.Created, (await i.Ingeniero.PostAsJsonAsync($"/api/v1/configuracion-campos/{i.TipoEquipo}", new { campos })).StatusCode);

    private static async Task<(Guid Orden, string Codigo)> OrdenAsignada(Institucion i)
    {
        var reporte = await i.Clinico.PostAsJsonAsync(RutaTiquetes, new { equipoId = i.Equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarla.", criticidad = Criticidades.Alta });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var tiquete = await Cuerpo(reporte);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        return ((await Cuerpo(asignada)).GetProperty("id").GetGuid(), Texto(tiquete, "codigo")!);
    }

    private async Task<Institucion> InstitucionConEquipo()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, _) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (clinico, _) = await SesionDe(tenant, CodigosRol.PersonalClinico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);

        var tipo = await api.CrearTipoEquipoAsync(tenant, "Bomba de infusión", ClasesRiesgo.IIb, true, 12);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Hospitalización");
        var registro = await ingeniero.PostAsJsonAsync("/api/v1/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = "SN-BOM-05",
            ["placaInventario"] = "BOM-05",
            ["marca"] = "B. Braun",
            ["modelo"] = "Infusomat Space",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2021DM-0007788",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return new Institucion(tenant, ingeniero, clinico, tecnico, tecnicoId, tipo, (await Cuerpo(registro)).GetProperty("id").GetGuid());
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static string? Texto(JsonElement elemento, string propiedad) => elemento.GetProperty(propiedad).GetString();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
