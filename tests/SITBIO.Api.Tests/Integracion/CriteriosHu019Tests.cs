using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-019 (CP-023) y HU-020 (CP-024) contra la API y PostgreSQL reales: el
/// Ingeniero define el plan preventivo del equipo; al consultar las alertas se generan las órdenes de los
/// planes cuya fecha llegó y se ven los mantenimientos y calibraciones por vencer o vencidos con los días de
/// anticipación de la institución (CA-HU-024-01). Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu019Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Planes = "/api/v1/planes-preventivos";
    private const string Alertas = "/api/v1/alertas";
    private const string Ordenes = "/api/v1/ordenes";

    // Las fechas de los planes son de Colombia (UTC-5, sin horario de verano), como en la API.
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, Guid IngenieroId, Guid Tipo, Guid Ubicacion);

    // ---------- CA-HU-019-01: el plan queda asociado al equipo ----------

    [FactIntegracion]
    public async Task CA_HU_019_01_ConPeriodicidadYProximaFecha_ElPlanQuedaAsociadoAlEquipo()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Equipo(i, "MON-01");

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Planes, new { equipoId = equipo, descripcion = "Preventivo mensual del monitor", periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-10) });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var plan = await Cuerpo(respuesta);
        var id = plan.GetProperty("id").GetGuid();
        Assert.EndsWith($"{Planes}/{id}", respuesta.Headers.Location!.OriginalString);
        Assert.Equal((equipo, "MON-01", 30), (plan.GetProperty("equipoId").GetGuid(), Texto(plan, "placaInventario"), plan.GetProperty("periodicidadDias").GetInt32()));
        Assert.Equal((Hoy.AddDays(20), PlanPreventivo.Activo, false), (Fecha(plan, "proximaFecha"), Texto(plan, "estado"), plan.GetProperty("vencido").GetBoolean()));

        // En la base, el plan es del equipo y de la institución.
        var guardado = Assert.Single(await api.LeerPlanesAsync(equipo));
        Assert.Equal((id, i.Tenant, Hoy.AddDays(-10), Hoy.AddDays(20)), (guardado.Id, guardado.TenantId, guardado.UltimaEjecucion!.Value, guardado.ProximaFecha));

        // Se consulta por el equipo y queda en la bitácora a nombre del Ingeniero.
        var lista = await Cuerpo(await i.Ingeniero.GetAsync($"{Planes}?equipoId={equipo}"));
        Assert.Equal([id], lista.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        var evento = Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e => e["entidad"].AsString == GestionPlanes.Entidad);
        Assert.Equal((AccionesAuditoria.Crear, id.ToString(), i.IngenieroId.ToString()), (evento["accion"].AsString, evento["entidad_id"].AsString, evento["usuario_id"].AsString));
    }

    [FactIntegracion]
    public async Task CA_HU_019_01_SinPeriodicidadOSinEquipoValido_NoSeGuarda_YSoloElIngenieroLoDefine()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Equipo(i, "MON-02");
        var deBaja = await Equipo(i, "MON-03");
        await api.DarDeBajaAsync(deBaja);
        var (tecnico, _) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);

        var sinPeriodicidad = await i.Ingeniero.PostAsJsonAsync(Planes, new { equipoId = equipo, proximaFecha = Hoy.AddDays(5) });
        Assert.Equal(HttpStatusCode.BadRequest, sinPeriodicidad.StatusCode);
        Assert.True((await Cuerpo(sinPeriodicidad)).GetProperty("errors").TryGetProperty("periodicidadDias", out _));

        var baja = await i.Ingeniero.PostAsJsonAsync(Planes, new { equipoId = deBaja, periodicidadDias = 30, proximaFecha = Hoy.AddDays(5) });
        Assert.Equal((HttpStatusCode.Conflict, GestionPlanes.CodigoEquipoDeBaja), (baja.StatusCode, Texto(await Cuerpo(baja), "codigo")));

        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync(Planes, new { equipoId = equipo, periodicidadDias = 30, proximaFecha = Hoy.AddDays(5) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tecnico.GetAsync(Planes)).StatusCode);

        Assert.Empty(await api.LeerPlanesAsync(equipo));
        Assert.Empty(await api.LeerPlanesAsync(deBaja));
    }

    // ---------- CA-HU-019-02: al llegar la fecha se genera la orden preventiva ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_019_02_AlLlegarLaFecha_SeGeneraLaOrdenPreventiva_YAlAprobarlaElPlanAvanza()
    {
        var i = await InstitucionConIngeniero();
        var (tecnico, tecnicoId) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var equipo = await Equipo(i, "BOM-01");
        var otro = await Equipo(i, "BOM-02");
        var llego = await CrearPlan(i, new { equipoId = equipo, periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-30) });
        await CrearPlan(i, new { equipoId = otro, periodicidadDias = 30, proximaFecha = Hoy.AddDays(5) });

        // La consulta de alertas evalúa los planes: solo el que llegó a su fecha genera la orden.
        var alertas = await Cuerpo(await tecnico.GetAsync(Alertas));
        Assert.Equal(1, alertas.GetProperty("ordenesGeneradas").GetInt32());

        var mias = (await Cuerpo(await tecnico.GetAsync($"{Ordenes}?asignadas=me"))).GetProperty("items").EnumerateArray().ToList();
        var orden = Assert.Single(mias);
        var ordenId = orden.GetProperty("id").GetGuid();
        Assert.Equal((llego, equipo, tecnicoId), (orden.GetProperty("planId").GetGuid(), orden.GetProperty("equipoId").GetGuid(), orden.GetProperty("tecnicoId").GetGuid()));
        Assert.Equal((TiposMantenimiento.Preventivo, EstadosOrden.Asignada, Hoy), (Texto(orden, "tipoMantenimiento"), Texto(orden, "estado"), Fecha(orden, "fechaProgramada")));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (orden.GetProperty("tiqueteId").ValueKind, orden.GetProperty("asignadoPorId").ValueKind));

        // La alerta del plan muestra su orden; consultar de nuevo no repite la orden.
        var alerta = Assert.Single(alertas.GetProperty("alertas").EnumerateArray(), a => a.GetProperty("planId").GetGuid() == llego);
        Assert.Equal((ordenId, EstadosOrden.Asignada), (alerta.GetProperty("ordenId").GetGuid(), Texto(alerta, "estadoOrden")));
        Assert.Equal(0, (await Cuerpo(await i.Ingeniero.GetAsync(Alertas))).GetProperty("ordenesGeneradas").GetInt32());
        Assert.Single((await Cuerpo(await i.Ingeniero.GetAsync($"{Ordenes}?tipo={TiposMantenimiento.Preventivo}"))).GetProperty("items").EnumerateArray());
        Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e => e["entidad"].AsString == GestionOrdenes.Entidad && e["accion"].AsString == AccionesAuditoria.Crear);

        // El técnico la ejecuta y la cierra; al aprobarla, el último preventivo es hoy y el próximo, hoy + la periodicidad.
        Assert.Equal(HttpStatusCode.OK, (await tecnico.PatchAsJsonAsync($"{Ordenes}/{ordenId}/estado", new { accion = AccionesOrden.Iniciar })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tecnico.PutAsJsonAsync($"{Ordenes}/{ordenId}/intervencion", Hoja())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tecnico.PatchAsJsonAsync($"{Ordenes}/{ordenId}/estado", new { accion = AccionesOrden.Cerrar })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PostAsJsonAsync($"{Ordenes}/{ordenId}/aprobacion", new { firma = "Laura Gómez, Ingeniera Biomédica" })).StatusCode);

        var plan = Assert.Single(await api.LeerPlanesAsync(equipo));
        Assert.Equal((Hoy, Hoy.AddDays(30), PlanPreventivo.Activo), (plan.UltimaEjecucion!.Value, plan.ProximaFecha, plan.Estado));
        Assert.Equal(0, (await Cuerpo(await i.Ingeniero.GetAsync(Alertas))).GetProperty("ordenesGeneradas").GetInt32());
    }

    [FactIntegracion]
    public async Task CA_HU_019_02_UnPlanSuspendidoOUnEquipoDeBaja_NoGeneranOrden()
    {
        var i = await InstitucionConIngeniero();
        await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var suspendido = await CrearPlan(i, new { equipoId = await Equipo(i, "VEN-01"), periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-30) });
        var deBaja = await Equipo(i, "VEN-02");
        await CrearPlan(i, new { equipoId = deBaja, periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-30) });
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PutAsJsonAsync($"{Planes}/{suspendido}", new { estado = PlanPreventivo.Suspendido })).StatusCode);
        await api.DarDeBajaAsync(deBaja);

        var alertas = await Cuerpo(await i.Ingeniero.GetAsync(Alertas));

        Assert.Equal(0, alertas.GetProperty("ordenesGeneradas").GetInt32());
        Assert.Empty(alertas.GetProperty("alertas").EnumerateArray());
        Assert.Empty((await Cuerpo(await i.Ingeniero.GetAsync(Ordenes))).GetProperty("items").EnumerateArray());
    }

    // ---------- CA-HU-020-01 y CA-HU-024-01: alerta dentro del periodo de anticipación ----------

    [FactIntegracion]
    public async Task CA_HU_020_01_DentroDelPeriodoDeAnticipacion_VeLaAlerta_ConLosDiasDeLaInstitucion()
    {
        var i = await InstitucionConIngeniero();
        var (tecnico, _) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var cercano = await CrearPlan(i, new { equipoId = await Equipo(i, "DEF-01"), periodicidadDias = 90, proximaFecha = Hoy.AddDays(10) });
        var lejano = await CrearPlan(i, new { equipoId = await Equipo(i, "DEF-02"), periodicidadDias = 90, proximaFecha = Hoy.AddDays(25) });
        var calibrado = await Equipo(i, "DEF-03", requiereCalibracion: true);
        await api.RegistrarCalibracionAsync(i.Tenant, calibrado, i.IngenieroId, Hoy.AddDays(-360), Hoy.AddDays(5));
        await DiasDeAnticipacion(i.Tenant, 20);

        var alertas = await Cuerpo(await tecnico.GetAsync(Alertas));

        Assert.Equal(20, alertas.GetProperty("diasAnticipacion").GetInt32());
        var lista = alertas.GetProperty("alertas").EnumerateArray().ToList();
        Assert.Equal(["DEF-03", "DEF-01"], lista.Select(a => Texto(a, "placaInventario")));
        Assert.All(lista, a => Assert.Equal(EstadosAlerta.Proxima, Texto(a, "estado")));
        Assert.Equal((TiposAlerta.Calibracion, 5, calibrado), (Texto(lista[0], "tipo"), lista[0].GetProperty("diasRestantes").GetInt32(), lista[0].GetProperty("equipoId").GetGuid()));
        Assert.Equal((TiposAlerta.Preventivo, 10, cercano), (Texto(lista[1], "tipo"), lista[1].GetProperty("diasRestantes").GetInt32(), lista[1].GetProperty("planId").GetGuid()));

        // Con 30 días, el plan de dentro de 25 también entra (CA-HU-024-01); aún no llega, así que no hay orden.
        await DiasDeAnticipacion(i.Tenant, 30);
        var conTreinta = await Cuerpo(await i.Ingeniero.GetAsync(Alertas));
        Assert.Equal(30, conTreinta.GetProperty("diasAnticipacion").GetInt32());
        var delLejano = Assert.Single(conTreinta.GetProperty("alertas").EnumerateArray(), a => Texto(a, "tipo") == TiposAlerta.Preventivo && a.GetProperty("planId").GetGuid() == lejano);
        Assert.Equal((EstadosAlerta.Proxima, 25, JsonValueKind.Null), (Texto(delLejano, "estado"), delLejano.GetProperty("diasRestantes").GetInt32(), delLejano.GetProperty("ordenId").ValueKind));
        Assert.Equal(0, conTreinta.GetProperty("ordenesGeneradas").GetInt32());
    }

    [FactIntegracion]
    public async Task CA_HU_020_01_SoloElIngenieroYElTecnicoVenLasAlertas()
    {
        var i = await InstitucionConIngeniero();

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var (cliente, _) = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Alertas)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.GetAsync(Alertas)).StatusCode);
    }

    // ---------- CA-HU-020-02: alerta vencida ----------

    [FactIntegracion]
    public async Task CA_HU_020_02_YaVencido_LaAlertaSeMuestraComoVencida_YVaPrimero()
    {
        var i = await InstitucionConIngeniero();
        var proximo = await CrearPlan(i, new { equipoId = await Equipo(i, "ECG-01"), periodicidadDias = 30, proximaFecha = Hoy.AddDays(3) });
        var vencido = await CrearPlan(i, new { equipoId = await Equipo(i, "ECG-02"), periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-40) });
        var calibrado = await Equipo(i, "ECG-03", requiereCalibracion: true);
        await api.RegistrarCalibracionAsync(i.Tenant, calibrado, i.IngenieroId, Hoy.AddDays(-368), Hoy.AddDays(-3));

        // Sin técnicos activos no se genera la orden: la alerta vencida se queda a la vista sin orden.
        var sinTecnicos = await Cuerpo(await i.Ingeniero.GetAsync(Alertas));
        Assert.Equal(0, sinTecnicos.GetProperty("ordenesGeneradas").GetInt32());
        var lista = sinTecnicos.GetProperty("alertas").EnumerateArray().ToList();
        Assert.Equal(["ECG-02", "ECG-03", "ECG-01"], lista.Select(a => Texto(a, "placaInventario")));
        Assert.Equal((EstadosAlerta.Vencida, -10, vencido, JsonValueKind.Null), (Texto(lista[0], "estado"), lista[0].GetProperty("diasRestantes").GetInt32(), lista[0].GetProperty("planId").GetGuid(), lista[0].GetProperty("ordenId").ValueKind));
        Assert.Equal((TiposAlerta.Calibracion, EstadosAlerta.Vencida, -3), (Texto(lista[1], "tipo"), Texto(lista[1], "estado"), lista[1].GetProperty("diasRestantes").GetInt32()));
        Assert.Equal((EstadosAlerta.Proxima, proximo), (Texto(lista[2], "estado"), lista[2].GetProperty("planId").GetGuid()));

        // Con un técnico activo se genera la orden y la alerta sigue «vencida» hasta que se apruebe.
        var (tecnico, tecnicoId) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var conTecnico = await Cuerpo(await tecnico.GetAsync(Alertas));
        Assert.Equal(1, conTecnico.GetProperty("ordenesGeneradas").GetInt32());
        var conOrden = conTecnico.GetProperty("alertas").EnumerateArray().First();
        Assert.Equal((vencido, EstadosAlerta.Vencida, EstadosOrden.Asignada), (conOrden.GetProperty("planId").GetGuid(), Texto(conOrden, "estado"), Texto(conOrden, "estadoOrden")));
        var orden = Assert.Single((await Cuerpo(await tecnico.GetAsync($"{Ordenes}?asignadas=me"))).GetProperty("items").EnumerateArray());
        Assert.Equal((conOrden.GetProperty("ordenId").GetGuid(), tecnicoId, Hoy.AddDays(-10)), (orden.GetProperty("id").GetGuid(), orden.GetProperty("tecnicoId").GetGuid(), Fecha(orden, "fechaProgramada")));
    }

    // ---------- CP-036: aislamiento entre instituciones ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoVeNiUsaPlanesNiAlertasAjenos()
    {
        var a = await InstitucionConIngeniero();
        var b = await InstitucionConIngeniero();
        await SesionDe(b.Tenant, CodigosRol.TecnicoMantenimiento);
        var equipoA = await Equipo(a, "INC-01");
        var planA = await CrearPlan(a, new { equipoId = equipoA, periodicidadDias = 30, ultimaEjecucion = Hoy.AddDays(-35) });

        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Planes}/{planA}")).StatusCode);
        Assert.Empty((await Cuerpo(await b.Ingeniero.GetAsync(Planes))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.PutAsJsonAsync($"{Planes}/{planA}", new { estado = PlanPreventivo.Suspendido })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.PostAsJsonAsync(Planes, new { equipoId = equipoA, periodicidadDias = 30, proximaFecha = Hoy.AddDays(5) })).StatusCode);

        // Las alertas de B no traen el plan vencido de A ni le generan una orden con sus técnicos.
        var alertasB = await Cuerpo(await b.Ingeniero.GetAsync(Alertas));
        Assert.Equal(0, alertasB.GetProperty("ordenesGeneradas").GetInt32());
        Assert.Empty(alertasB.GetProperty("alertas").EnumerateArray());

        var plan = Assert.Single(await api.LeerPlanesAsync(equipoA));
        Assert.Equal((PlanPreventivo.Activo, 1), (plan.Estado, (await api.LeerPlanesAsync(equipoA)).Count));
    }

    // ---------- Apoyo ----------

    private static object Hoja() => new
    {
        diagnostico = "Equipo en buen estado; desgaste normal de los filtros",
        accionesRealizadas = "Limpieza, cambio de filtros y prueba funcional con el analizador",
        repuestos = new[] { new { nombre = "Filtro de aire", cantidad = 1, costoUnitario = 12000 } },
        costoManoObra = 35000,
        tiempoAtencionMin = 60,
        estadoEquipoSalida = EstadosSalidaEquipo.Operativo,
    };

    private static async Task<Guid> CrearPlan(Institucion i, object datos)
    {
        var respuesta = await i.Ingeniero.PostAsJsonAsync(Planes, datos);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Cuerpo(respuesta)).GetProperty("id").GetGuid();
    }

    /// <summary>Días de anticipación guardados por el Administrador en su institución (HU-024).</summary>
    private async Task DiasDeAnticipacion(Guid tenant, int dias)
    {
        var (admin, _) = await SesionDe(tenant, CodigosRol.AdministradorSistema);
        var nit = $"9{Random.Shared.Next(0, 100):00}.{Random.Shared.Next(0, 1000):000}.{Random.Shared.Next(0, 1000):000}-{Random.Shared.Next(0, 10)}";
        var respuesta = await admin.PutAsJsonAsync("/api/v1/tenant/actual", new { nombre = "Clínica San Rafael", nit, diasAnticipacionAlerta = dias });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    /// <summary>Equipo registrado por la API; los de calibración son clase IIb con periodicidad de 12 meses.</summary>
    private static async Task<Guid> Equipo(Institucion i, string placa, bool requiereCalibracion = false)
    {
        var registro = await i.Ingeniero.PostAsJsonAsync("/api/v1/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = i.Tipo,
            ["ubicacionId"] = i.Ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = "Mindray",
            ["modelo"] = "BeneView T5",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = $"INVIMA 2021DM-{placa}",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = requiereCalibracion ? ClasesRiesgo.IIb : ClasesRiesgo.I,
            ["requiereCalibracion"] = requiereCalibracion,
            ["periodicidadCalibracionMeses"] = requiereCalibracion ? 12 : null,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return (await Cuerpo(registro)).GetProperty("id").GetGuid();
    }

    private async Task<Institucion> InstitucionConIngeniero()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, ingenieroId) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.I, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        return new Institucion(tenant, ingeniero, ingenieroId, tipo, ubicacion);
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static DateOnly Fecha(JsonElement elemento, string propiedad) =>
        DateOnly.Parse(elemento.GetProperty(propiedad).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static string? Texto(JsonElement elemento, string propiedad) => elemento.GetProperty(propiedad).GetString();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
