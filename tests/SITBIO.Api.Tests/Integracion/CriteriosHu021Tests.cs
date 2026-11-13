using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-021 (CP-025) contra la API, PostgreSQL y MongoDB reales: el Técnico cierra
/// su orden con la intervención registrada y queda pendiente de aprobación; el Ingeniero la aprueba con su
/// firma y la orden y su intervención quedan inalterables (RN-004). Se comprueban también el fin de RN-005
/// (el equipo toma su estado de salida) y RN-007 (calibración vencida). Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu021Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/ordenes";
    private const string RutaTiquetes = "/api/v1/tiquetes";
    private const string FallaNoEnciende = "No enciende";
    private const string Firma = "Laura Gómez, Ingeniera Biomédica";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Institucion(
        Guid Tenant,
        HttpClient Ingeniero,
        Guid IngenieroId,
        HttpClient Clinico,
        HttpClient Tecnico,
        Guid TecnicoId,
        Guid Equipo);

    // ---------- CA-HU-021-01: cierre por el técnico ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_021_01_ConDiagnosticoYAcciones_ElTecnicoCierra_YQuedaPendienteDeAprobacion()
    {
        var i = await InstitucionConEquipo();
        var (orden, codigo) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);

        // Sin la intervención no se cierra.
        var sinIntervencion = await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);
        Assert.Equal(HttpStatusCode.Conflict, sinIntervencion.StatusCode);
        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, Texto(await Cuerpo(sinIntervencion), "codigo"));

        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja())).StatusCode);
        var cerrada = await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);

        Assert.Equal(HttpStatusCode.OK, cerrada.StatusCode);
        var dto = await Cuerpo(cerrada);
        Assert.Equal(EstadosOrden.Cerrada, Texto(dto, "estado"));
        Assert.Equal(JsonValueKind.String, dto.GetProperty("fechaCierre").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("firmaAprobacion").ValueKind);

        // Queda pendiente de aprobación: el Ingeniero la encuentra entre las cerradas.
        var pendientes = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}?estado={EstadosOrden.Cerrada}"));
        Assert.Equal([orden], pendientes.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));

        // Cerrada, el técnico ya no la cambia ni la cierra de nuevo; el tiquete sigue «en ejecución» hasta la aprobación.
        var corregir = await Registrar(i.Tecnico, orden, Hoja());
        Assert.Equal((HttpStatusCode.Conflict, GestionEjecucion.CodigoOrdenCerrada), (corregir.StatusCode, Texto(await Cuerpo(corregir), "codigo")));
        Assert.Equal(GestionEjecucion.CodigoOrdenCerrada, Texto(await Cuerpo(await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)), "codigo"));
        Assert.Equal(GestionEjecucion.CodigoTransicionInvalida, Texto(await Cuerpo(await Estado(i.Tecnico, orden, AccionesOrden.Iniciar)), "codigo"));
        Assert.Equal(EstadosTiquete.EnEjecucion, Texto(await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}")), "estado"));

        // El cierre queda en la bitácora a nombre del técnico.
        var cierre = Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e =>
            e["entidad"].AsString == GestionOrdenes.Entidad && e["detalle"].AsBsonDocument.GetValue("accion", "").AsString == AccionesOrden.Cerrar);
        Assert.Equal((AccionesAuditoria.Actualizar, i.TecnicoId.ToString()), (cierre["accion"].AsString, cierre["usuario_id"].AsString));
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_021_01_SinEstadoDeSalidaDelEquipo_NoSeCierra()
    {
        var i = await InstitucionConEquipo();
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja(salida: null))).StatusCode);

        var respuesta = await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.True((await Cuerpo(respuesta)).GetProperty("errors").TryGetProperty("estadoEquipoSalida", out _));
        Assert.Equal(EstadosOrden.EnEjecucion, Texto(await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}/{orden}")), "estado"));
    }

    // ---------- CA-HU-021-02: aprobación con firma ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_021_02_ElIngenieroApruebaConSuFirma_YLaOrdenYSuIntervencionQuedanInalterables()
    {
        var i = await InstitucionConEquipo();

        // RN-005: la falla crítica deja el equipo «Fuera de servicio» hasta que se apruebe la orden.
        var (orden, codigo) = await OrdenAsignada(i, Criticidades.Critica);
        Assert.Equal(EstadosEquipo.FueraDeServicio, (await api.LeerEquipoAsync(i.Equipo)).EstadoEquipoId);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);

        var aprobada = await Aprobar(i.Ingeniero, orden, Firma);

        Assert.Equal(HttpStatusCode.OK, aprobada.StatusCode);
        var dto = await Cuerpo(aprobada);
        Assert.Equal((EstadosOrden.Aprobada, Firma, i.IngenieroId), (Texto(dto, "estado"), Texto(dto, "firmaAprobacion"), dto.GetProperty("aprobadoPorId").GetGuid()));
        Assert.Equal(JsonValueKind.String, dto.GetProperty("fechaAprobacion").ValueKind);

        // La intervención quedó firmada en MongoDB con su estado de salida y la copia del equipo.
        var documento = Assert.Single(await api.LeerIntervencionesAsync(i.Tenant));
        Assert.Equal((true, EstadosSalidaEquipo.Operativo), (documento["cerrada_firmada"].AsBoolean, documento["estado_equipo_salida"].AsString));
        var copia = documento["equipo_snapshot"];
        Assert.Equal(("Bomba de infusión BOM-05", "B. Braun", "Infusomat Space", "Bomba de infusión", "Hospitalización"),
            (copia["nombre"].AsString, copia["marca"].AsString, copia["modelo"].AsString, copia["tipo_equipo"].AsString, copia["ubicacion"].AsString));
        Assert.True((await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{orden}/intervencion"))).GetProperty("cerradaFirmada").GetBoolean());

        // El tiquete se cierra a nombre del Ingeniero y el equipo vuelve a «Operativo» (fin de RN-005).
        var tiquete = await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}"));
        Assert.Equal((EstadosTiquete.Cerrado, JsonValueKind.String), (Texto(tiquete, "estado"), tiquete.GetProperty("fechaCierre").ValueKind));
        var ultimo = (await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}/historial"))).EnumerateArray().Last();
        Assert.Equal((EstadosTiquete.EnEjecucion, EstadosTiquete.Cerrado, i.IngenieroId), (Texto(ultimo, "estadoAnterior"), Texto(ultimo, "nuevoEstado"), ultimo.GetProperty("usuarioId").GetGuid()));
        Assert.Equal(EstadosEquipo.Operativo, (await api.LeerEquipoAsync(i.Equipo)).EstadoEquipoId);
        var cambioEquipo = (await api.LeerHistorialAsync(i.Equipo)).Single(h => h.NuevoEstadoId == EstadosEquipo.Operativo && h.EstadoAnteriorId == EstadosEquipo.FueraDeServicio);
        Assert.Equal(i.IngenieroId, cambioEquipo.UsuarioId);

        // La aprobación queda en la bitácora como cierre de la orden.
        var evento = Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e => e["accion"].AsString == AccionesAuditoria.CierreOt);
        Assert.Equal((orden.ToString(), i.IngenieroId.ToString(), documento["_id"].AsObjectId.ToString()),
            (evento["entidad_id"].AsString, evento["usuario_id"].AsString, evento["detalle"]["intervencion"].AsString));

        // RN-004: nadie la modifica ni la vuelve a firmar.
        foreach (var intento in new[]
        {
            await Registrar(i.Tecnico, orden, Hoja(diagnostico: "Cambio después de firmar")),
            await Estado(i.Tecnico, orden, AccionesOrden.Cerrar),
            await Aprobar(i.Ingeniero, orden, "Otra firma"),
        })
        {
            Assert.Equal((HttpStatusCode.Conflict, GestionEjecucion.CodigoFirmada), (intento.StatusCode, Texto(await Cuerpo(intento), "codigo")));
        }

        var despues = Assert.Single(await api.LeerIntervencionesAsync(i.Tenant));
        Assert.Equal(documento["diagnostico"].AsString, despues["diagnostico"].AsString);
        Assert.Equal(Firma, Texto(await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{orden}")), "firmaAprobacion"));
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_021_02_SoloSeApruebaUnaOrdenCerrada_YConFirma()
    {
        var i = await InstitucionConEquipo();
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja())).StatusCode);

        var abierta = await Aprobar(i.Ingeniero, orden, Firma);
        Assert.Equal((HttpStatusCode.Conflict, GestionEjecucion.CodigoTransicionInvalida), (abierta.StatusCode, Texto(await Cuerpo(abierta), "codigo")));

        await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);
        var sinFirma = await Aprobar(i.Ingeniero, orden, "  ");
        Assert.Equal(HttpStatusCode.BadRequest, sinFirma.StatusCode);
        Assert.True((await Cuerpo(sinFirma)).GetProperty("errors").TryGetProperty("firma", out _));

        Assert.Equal(EstadosOrden.Cerrada, Texto(await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{orden}")), "estado"));
        Assert.False(Assert.Single(await api.LeerIntervencionesAsync(i.Tenant))["cerrada_firmada"].AsBoolean);
    }

    // ---------- RN-007: calibración vencida ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task Rn007_ConLaCalibracionVencida_NoSaleOperativo_PeroSiConOtroEstado()
    {
        var i = await InstitucionConEquipo();
        await api.RegistrarCalibracionAsync(i.Tenant, i.Equipo, i.IngenieroId, Hoy.AddYears(-1).AddDays(-10), Hoy.AddDays(-10));
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja())).StatusCode);

        var operativo = await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);

        Assert.Equal(HttpStatusCode.Conflict, operativo.StatusCode);
        var problema = await Cuerpo(operativo);
        Assert.Equal(ReglaCalibracion.Codigo, Texto(problema, "codigo"));
        Assert.Contains($"venció el {Hoy.AddDays(-10).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}", Texto(problema, "detail"));
        Assert.Equal(EstadosOrden.EnEjecucion, Texto(await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}/{orden}")), "estado"));

        // Con otro estado de salida sí se cierra y se aprueba; el equipo queda «Fuera de servicio».
        Assert.Equal(HttpStatusCode.OK, (await Registrar(i.Tecnico, orden, Hoja(salida: EstadosSalidaEquipo.FueraDeServicio))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Aprobar(i.Ingeniero, orden, Firma)).StatusCode);
        Assert.Equal(EstadosEquipo.FueraDeServicio, (await api.LeerEquipoAsync(i.Equipo)).EstadoEquipoId);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task Rn007_ConLaCalibracionVigente_SaleOperativo()
    {
        var i = await InstitucionConEquipo();
        await api.RegistrarCalibracionAsync(i.Tenant, i.Equipo, i.IngenieroId, Hoy.AddDays(-30), Hoy.AddYears(1));
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        await Registrar(i.Tecnico, orden, Hoja());

        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Aprobar(i.Ingeniero, orden, Firma)).StatusCode);
    }

    // ---------- Permisos y aislamiento ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task Permisos_SoloElTecnicoCierraYSoloElIngenieroAprueba_YOtraInstitucionNoLaVe()
    {
        var i = await InstitucionConEquipo();
        var (orden, _) = await OrdenAsignada(i);
        await Estado(i.Tecnico, orden, AccionesOrden.Iniciar);
        await Registrar(i.Tecnico, orden, Hoja());

        // Otro técnico no la cierra (404); el Ingeniero tampoco (403).
        var (otroTecnico, _) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        Assert.Equal(HttpStatusCode.NotFound, (await Estado(otroTecnico, orden, AccionesOrden.Cerrar)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Estado(i.Ingeniero, orden, AccionesOrden.Cerrar)).StatusCode);
        await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);

        // Solo el Ingeniero aprueba: el técnico y el Personal Clínico reciben 403; el Ingeniero de otra institución, 404.
        Assert.Equal(HttpStatusCode.Forbidden, (await Aprobar(i.Tecnico, orden, Firma)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Aprobar(i.Clinico, orden, Firma)).StatusCode);
        var (ingenieroAjeno, _) = await SesionDe(api.TenantB, CodigosRol.IngenieroBiomedico);
        Assert.Equal(HttpStatusCode.NotFound, (await Aprobar(ingenieroAjeno, orden, Firma)).StatusCode);

        Assert.Equal(EstadosOrden.Cerrada, Texto(await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{orden}")), "estado"));
        Assert.False(Assert.Single(await api.LeerIntervencionesAsync(i.Tenant))["cerrada_firmada"].AsBoolean);
    }

    // ---------- Apoyo ----------

    private static object Hoja(string? salida = EstadosSalidaEquipo.Operativo, string diagnostico = "Sensor de presión descalibrado") => new
    {
        diagnostico,
        accionesRealizadas = "Se calibró el sensor y se verificó con el analizador",
        repuestos = new[] { new { nombre = "Sensor de presión", cantidad = 1, costoUnitario = 15000 } },
        costoManoObra = 40000,
        tiempoAtencionMin = 90,
        estadoEquipoSalida = salida,
    };

    private static Task<HttpResponseMessage> Estado(HttpClient cliente, Guid orden, string accion) =>
        cliente.PatchAsJsonAsync($"{Ruta}/{orden}/estado", new { accion });

    private static Task<HttpResponseMessage> Registrar(HttpClient cliente, Guid orden, object hoja) =>
        cliente.PutAsJsonAsync($"{Ruta}/{orden}/intervencion", hoja);

    private static Task<HttpResponseMessage> Aprobar(HttpClient cliente, Guid orden, string firma) =>
        cliente.PostAsJsonAsync($"{Ruta}/{orden}/aprobacion", new { firma });

    private static async Task<(Guid Orden, string Codigo)> OrdenAsignada(Institucion i, string criticidad = Criticidades.Alta)
    {
        var reporte = await i.Clinico.PostAsJsonAsync(RutaTiquetes, new { equipoId = i.Equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarla.", criticidad });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var tiquete = await Cuerpo(reporte);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        return ((await Cuerpo(asignada)).GetProperty("id").GetGuid(), Texto(tiquete, "codigo")!);
    }

    private async Task<Institucion> InstitucionConEquipo()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, ingenieroId) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
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
        return new Institucion(tenant, ingeniero, ingenieroId, clinico, tecnico, tecnicoId, (await Cuerpo(registro)).GetProperty("id").GetGuid());
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
