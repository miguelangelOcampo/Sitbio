using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Mantenimiento;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-022 (CP-026) contra la API, PostgreSQL y MongoDB reales: el Técnico registra
/// la calibración con fecha, patrón, resultado y vencimiento y queda en el historial del equipo
/// (CA-HU-022-01); con la calibración vencida la orden no sale «Operativo» hasta registrar una nueva
/// (CA-HU-022-02, RN-007); un equipo que no requiere calibración no se calibra, no genera alertas y no se le
/// aplica RN-007 (CA-HU-022-03, RN-010). Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu022Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Calibraciones = "/api/v1/calibraciones";
    private const string Ordenes = "/api/v1/ordenes";
    private const string Alertas = "/api/v1/alertas";
    private const string FallaNoEnciende = "No enciende";

    // Las fechas de las calibraciones son de Colombia (UTC-5, sin horario de verano), como en la API.
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(
        Guid Tenant,
        HttpClient Ingeniero,
        Guid IngenieroId,
        HttpClient Tecnico,
        Guid TecnicoId,
        Guid Tipo,
        Guid Ubicacion);

    // ---------- CA-HU-022-01: la calibración queda en el historial con su vencimiento ----------

    [FactIntegracion]
    public async Task CA_HU_022_01_CalibracionCompleta_QuedaEnElHistorialDelEquipoConSuVencimiento()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "MON-01", requiereCalibracion: true);
        var laboratorio = await api.CrearLaboratorioAsync(i.Tenant, "Metrología Andina");

        var respuesta = await i.Tecnico.PostAsJsonAsync(Calibraciones, new
        {
            equipoId = equipo,
            fechaCalibracion = Hoy.AddDays(-2),
            patronReferencia = "Simulador de paciente Fluke ProSim 8",
            resultado = RegistroCalibracion.Conforme,
            numeroCertificado = "CERT-2026-0451",
            proveedorId = laboratorio,
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var calibracion = await Cuerpo(respuesta);
        var id = calibracion.GetProperty("id").GetGuid();
        Assert.EndsWith($"{Calibraciones}/{id}", respuesta.Headers.Location!.OriginalString);

        // Sin vencimiento indicado: la fecha + los 12 meses del equipo (RN-010).
        Assert.Equal((Hoy.AddDays(-2), Hoy.AddDays(-2).AddMonths(12), true), (Fecha(calibracion, "fechaCalibracion"), Fecha(calibracion, "fechaVencimiento"), calibracion.GetProperty("vigente").GetBoolean()));
        Assert.Equal(("MON-01", "Metrología Andina", "CERT-2026-0451", false), (Texto(calibracion, "placaInventario"), Texto(calibracion, "proveedor"), Texto(calibracion, "numeroCertificado"), calibracion.GetProperty("aprobada").GetBoolean()));

        // En la base, del equipo y de la institución, a nombre del Técnico; y en la bitácora.
        var guardada = Assert.Single(await api.LeerCalibracionesAsync(equipo));
        Assert.Equal((id, i.Tenant, i.TecnicoId, "Simulador de paciente Fluke ProSim 8"), (guardada.Id, guardada.TenantId, guardada.RegistradoPorId, guardada.PatronReferencia));
        var evento = Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e => e["entidad"].AsString == GestionCalibraciones.Entidad);
        Assert.Equal((AccionesAuditoria.Crear, id.ToString(), i.TecnicoId.ToString()), (evento["accion"].AsString, evento["entidad_id"].AsString, evento["usuario_id"].AsString));

        // Una segunda calibración con el vencimiento del certificado: el historial las muestra, la más reciente primero.
        var segunda = await Registrar(i.Tecnico, new { equipoId = equipo, fechaCalibracion = Hoy, patronReferencia = "Analizador Fluke IDA-5", resultado = "No conforme", fechaVencimiento = Hoy.AddMonths(6) });
        var historial = (await Cuerpo(await i.Ingeniero.GetAsync($"{Calibraciones}?equipoId={equipo}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([segunda, id], historial.Select(c => c.GetProperty("id").GetGuid()));
        Assert.Equal((RegistroCalibracion.NoConforme, Hoy.AddMonths(6)), (Texto(historial[0], "resultado"), Fecha(historial[0], "fechaVencimiento")));

        // El Ingeniero la aprueba; aprobada, ya no se corrige.
        var aprobada = await Cuerpo(await i.Ingeniero.PatchAsync($"{Calibraciones}/{id}/aprobacion", null));
        Assert.Equal((true, i.IngenieroId), (aprobada.GetProperty("aprobada").GetBoolean(), aprobada.GetProperty("aprobadoPorId").GetGuid()));
        var corregir = await i.Tecnico.PatchAsJsonAsync($"{Calibraciones}/{id}", new { numeroCertificado = "OTRO" });
        Assert.Equal((HttpStatusCode.Conflict, GestionCalibraciones.CodigoAprobada), (corregir.StatusCode, Texto(await Cuerpo(corregir), "codigo")));
    }

    [FactIntegracion]
    public async Task CA_HU_022_01_Incompleta_NoSeGuarda_YSoloElTecnicoRegistra()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "MON-02", requiereCalibracion: true);
        var (gerente, _) = await SesionDe(i.Tenant, CodigosRol.GerenteBiomedico);

        var incompleta = await i.Tecnico.PostAsJsonAsync(Calibraciones, new { equipoId = equipo, fechaCalibracion = Hoy.AddDays(1), resultado = "conforme con observaciones" });
        Assert.Equal(HttpStatusCode.BadRequest, incompleta.StatusCode);
        var errores = (await Cuerpo(incompleta)).GetProperty("errors");
        Assert.All(new[] { "fechaCalibracion", "patronReferencia", "resultado" }, campo => Assert.True(errores.TryGetProperty(campo, out _), campo));

        var anterior = await i.Tecnico.PostAsJsonAsync(Calibraciones, new { equipoId = equipo, fechaCalibracion = Hoy, patronReferencia = "Analizador", fechaVencimiento = Hoy.AddDays(-1) });
        Assert.True((await Cuerpo(anterior)).GetProperty("errors").TryGetProperty("fechaVencimiento", out _));

        var datos = new { equipoId = equipo, fechaCalibracion = Hoy, patronReferencia = "Analizador" };
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Ingeniero.PostAsJsonAsync(Calibraciones, datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gerente.PostAsJsonAsync(Calibraciones, datos)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gerente.GetAsync(Calibraciones)).StatusCode);

        Assert.Empty(await api.LeerCalibracionesAsync(equipo));
    }

    // ---------- CA-HU-022-02: con la calibración vencida no sale «Operativo» hasta registrar una nueva ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_022_02_ConLaCalibracionVencida_NoCierraOperativo_HastaRegistrarUnaNueva()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "BOM-01", requiereCalibracion: true);
        await Registrar(i.Tecnico, new { equipoId = equipo, fechaCalibracion = Hoy.AddYears(-1).AddDays(-10), patronReferencia = "Analizador Fluke IDA-5", fechaVencimiento = Hoy.AddDays(-10) });
        var orden = await OrdenEnEjecucion(i, equipo);

        var bloqueada = await Estado(i.Tecnico, orden, AccionesOrden.Cerrar);
        Assert.Equal(HttpStatusCode.Conflict, bloqueada.StatusCode);
        var problema = await Cuerpo(bloqueada);
        Assert.Equal(ReglaCalibracion.Codigo, Texto(problema, "codigo"));
        Assert.Contains($"venció el {Hoy.AddDays(-10).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}", Texto(problema, "detail"));

        // La alerta de calibración vencida está a la vista.
        var alerta = Assert.Single((await Cuerpo(await i.Tecnico.GetAsync(Alertas))).GetProperty("alertas").EnumerateArray(), a => Texto(a, "tipo") == TiposAlerta.Calibracion);
        Assert.Equal((equipo, EstadosAlerta.Vencida), (alerta.GetProperty("equipoId").GetGuid(), Texto(alerta, "estado")));

        // Con la nueva calibración, la misma orden sale «Operativo» y la alerta desaparece.
        await Registrar(i.Tecnico, new { equipoId = equipo, fechaCalibracion = Hoy, patronReferencia = "Analizador Fluke IDA-5" });
        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PostAsJsonAsync($"{Ordenes}/{orden}/aprobacion", new { firma = "Laura Gómez, Ingeniera Biomédica" })).StatusCode);
        Assert.Equal(EstadosEquipo.Operativo, (await api.LeerEquipoAsync(equipo)).EstadoEquipoId);
        Assert.DoesNotContain((await Cuerpo(await i.Tecnico.GetAsync(Alertas))).GetProperty("alertas").EnumerateArray(), a => Texto(a, "tipo") == TiposAlerta.Calibracion);
    }

    // ---------- CA-HU-022-03: un equipo que no requiere calibración (RN-010) ----------

    [FactIntegracion]
    public async Task CA_HU_022_03_EquipoQueNoRequiereCalibracion_NoSeCalibra_NiGeneraAlertas()
    {
        var i = await NuevaInstitucion();
        var estetoscopio = await Equipo(i, "EST-01");

        var respuesta = await i.Tecnico.PostAsJsonAsync(Calibraciones, new { equipoId = estetoscopio, fechaCalibracion = Hoy, patronReferencia = "Analizador" });

        Assert.Equal((HttpStatusCode.Conflict, GestionCalibraciones.CodigoNoRequiere), (respuesta.StatusCode, Texto(await Cuerpo(respuesta), "codigo")));
        Assert.Empty(await api.LeerCalibracionesAsync(estetoscopio));

        // Aunque tenga una calibración vencida de antes, no genera alerta.
        await api.RegistrarCalibracionAsync(i.Tenant, estetoscopio, i.TecnicoId, Hoy.AddYears(-1).AddDays(-5), Hoy.AddDays(-5));
        Assert.Empty((await Cuerpo(await i.Ingeniero.GetAsync(Alertas))).GetProperty("alertas").EnumerateArray());
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_022_03_EquipoQueNoRequiereCalibracion_NoSeLeAplicaRn007()
    {
        var i = await NuevaInstitucion();
        var estetoscopio = await Equipo(i, "EST-02");
        await api.RegistrarCalibracionAsync(i.Tenant, estetoscopio, i.TecnicoId, Hoy.AddYears(-1).AddDays(-5), Hoy.AddDays(-5));
        var orden = await OrdenEnEjecucion(i, estetoscopio);

        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);
    }

    // ---------- CP-036: aislamiento entre instituciones ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoVeNiCalibraNiApruebaLoAjeno()
    {
        var a = await NuevaInstitucion();
        var b = await NuevaInstitucion();
        var equipoA = await Equipo(a, "INC-01", requiereCalibracion: true);
        var calibracionA = await Registrar(a.Tecnico, new { equipoId = equipoA, fechaCalibracion = Hoy, patronReferencia = "Analizador" });

        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Calibraciones}/{calibracionA}")).StatusCode);
        Assert.Empty((await Cuerpo(await b.Tecnico.GetAsync($"{Calibraciones}?equipoId={equipoA}"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.PatchAsync($"{Calibraciones}/{calibracionA}/aprobacion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Tecnico.PatchAsJsonAsync($"{Calibraciones}/{calibracionA}", new { numeroCertificado = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Tecnico.PostAsJsonAsync(Calibraciones, new { equipoId = equipoA, fechaCalibracion = Hoy, patronReferencia = "Analizador" })).StatusCode);

        var guardada = Assert.Single(await api.LeerCalibracionesAsync(equipoA));
        Assert.Equal((false, (string?)null), (guardada.Aprobada, guardada.NumeroCertificado));
    }

    // ---------- Apoyo ----------

    private static async Task<Guid> Registrar(HttpClient tecnico, object datos)
    {
        var respuesta = await tecnico.PostAsJsonAsync(Calibraciones, datos);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Cuerpo(respuesta)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Estado(HttpClient cliente, Guid orden, string accion) =>
        cliente.PatchAsJsonAsync($"{Ordenes}/{orden}/estado", new { accion });

    /// <summary>Orden correctiva del equipo, iniciada por el Técnico y con la intervención registrada para salir «Operativo».</summary>
    private async Task<Guid> OrdenEnEjecucion(Institucion i, Guid equipo)
    {
        var (clinico, _) = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        await api.CrearTipoFallaAsync(i.Tenant, FallaNoEnciende);
        var reporte = await clinico.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarlo.", criticidad = Criticidades.Alta });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ordenes, new { tiqueteId = (await Cuerpo(reporte)).GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        var orden = (await Cuerpo(asignada)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Iniciar)).StatusCode);
        var hoja = new
        {
            diagnostico = "Sensor de presión descalibrado",
            accionesRealizadas = "Se ajustó el sensor y se verificó con el analizador",
            repuestos = new[] { new { nombre = "Sensor de presión", cantidad = 1, costoUnitario = 15000 } },
            costoManoObra = 40000,
            tiempoAtencionMin = 90,
            estadoEquipoSalida = EstadosSalidaEquipo.Operativo,
        };
        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.PutAsJsonAsync($"{Ordenes}/{orden}/intervencion", hoja)).StatusCode);
        return orden;
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

    private async Task<Institucion> NuevaInstitucion()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, ingenieroId) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.I, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        return new Institucion(tenant, ingeniero, ingenieroId, tecnico, tecnicoId, tipo, ubicacion);
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static DateOnly Fecha(JsonElement elemento, string propiedad) =>
        DateOnly.Parse(elemento.GetProperty(propiedad).GetString()!, CultureInfo.InvariantCulture);

    private static string? Texto(JsonElement elemento, string propiedad) => elemento.GetProperty(propiedad).GetString();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
