using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Auditoria;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-003 y HU-018 (DOC-04; CP-006 y CP-022) y aislamiento entre
/// instituciones (CP-036) contra la API, PostgreSQL y MongoDB reales: los usuarios inician sesión de
/// verdad, el equipo se registra con API-EQ y el tiquete, su historial, el estado del equipo y la
/// bitácora se comprueban en la base. Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu003Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/tiquetes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, HttpClient Clinico, Guid ClinicoId, Guid Equipo);

    // ---------- CA-HU-003-01 (CP-006) ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_003_01_ReporteConEquipoYTipoDeFalla_GeneraCodigoUnicoYLlegaAlPanelDelIngeniero()
    {
        var i = await InstitucionConEquipo();

        var primero = await Reportar(i.Clinico, Falla(i.Equipo, Criticidades.Media));
        var segundo = await Reportar(i.Clinico, Falla(i.Equipo, Criticidades.Baja));

        Assert.Equal("TQT-000001", primero.GetProperty("codigo").GetString());
        Assert.Equal("TQT-000002", segundo.GetProperty("codigo").GetString());
        Assert.Equal(EstadosTiquete.Recibido, primero.GetProperty("estado").GetString());
        Assert.Equal(i.ClinicoId, primero.GetProperty("reportanteId").GetGuid());

        // El Ingeniero Biomédico ve los tiquetes recibidos en su bandeja (UI-011), del más reciente al más antiguo.
        var bandeja = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}?estado={EstadosTiquete.Recibido}"));
        Assert.Equal(2, bandeja.GetProperty("total").GetInt32());
        Assert.Equal(["TQT-000002", "TQT-000001"], bandeja.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));

        // En PostgreSQL: el tiquete con quien lo reportó y su primer registro de historial.
        var (tiquete, historial) = (await api.LeerTiquetesAsync(i.Tenant))[0];
        Assert.Equal((i.ClinicoId, i.Equipo, FallaNoEnciende), (tiquete.ReportanteId, tiquete.EquipoId, tiquete.TipoFalla));
        var recepcion = Assert.Single(historial);
        Assert.Equal(((string?)null, EstadosTiquete.Recibido, i.ClinicoId), (recepcion.EstadoAnterior, recepcion.NuevoEstado, recepcion.UsuarioId));

        // En la bitácora: CREAR del tiquete con su código, sin datos personales.
        var eventos = (await api.LeerBitacoraAsync(i.Tenant)).Where(e => e["entidad"].AsString == "tiquete").ToList();
        Assert.Equal(2, eventos.Count);
        Assert.All(eventos, e => Assert.Equal(AccionesAuditoria.Crear, e["accion"].AsString));
        Assert.Contains(eventos, e => e["detalle"]["codigo"].AsString == "TQT-000001");
    }

    // ---------- CA-HU-003-02 (CP-006) ----------

    [FactIntegracion]
    public async Task CA_HU_003_02_SinEquipo_MuestraValidacionEnEspanolYNoGeneraTiquete()
    {
        var i = await InstitucionConEquipo();

        var respuesta = await i.Clinico.PostAsJsonAsync(Ruta, new { tipoFalla = FallaNoEnciende, descripcion = "No prende.", criticidad = Criticidades.Alta });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var errores = (await Cuerpo(respuesta)).GetProperty("errors");
        Assert.Equal("Selecciona el equipo que presenta la falla.", errores.GetProperty("equipoId")[0].GetString());
        Assert.Empty(await api.LeerTiquetesAsync(i.Tenant));

        // Tampoco con un tipo de falla que no está en el catálogo, ni sin descripción ni criticidad.
        var incompleto = await i.Clinico.PostAsJsonAsync(Ruta, new { equipoId = i.Equipo, tipoFalla = "Se cayó" });
        Assert.Equal(HttpStatusCode.BadRequest, incompleto.StatusCode);
        var faltantes = (await Cuerpo(incompleto)).GetProperty("errors");
        Assert.True(faltantes.TryGetProperty("tipoFalla", out _));
        Assert.True(faltantes.TryGetProperty("descripcion", out _));
        Assert.True(faltantes.TryGetProperty("criticidad", out _));
        Assert.Empty(await api.LeerTiquetesAsync(i.Tenant));
    }

    // ---------- CA-HU-018-01 (CP-022) ----------

    [FactIntegracion]
    public async Task CA_HU_018_01_LaCriticidadSeGuardaConElTiqueteYSugiereLaPrioridad()
    {
        var i = await InstitucionConEquipo();

        foreach (var criticidad in new[] { Criticidades.Alta, Criticidades.Media, Criticidades.Baja })
        {
            var creado = await Reportar(i.Clinico, Falla(i.Equipo, criticidad));
            Assert.Equal(criticidad, creado.GetProperty("criticidad").GetString());
            Assert.Equal(criticidad, creado.GetProperty("prioridadSugerida").GetString());
            Assert.False(creado.GetProperty("equipoFueraDeServicio").GetBoolean());
        }

        Assert.Equal([Criticidades.Alta, Criticidades.Media, Criticidades.Baja], (await api.LeerTiquetesAsync(i.Tenant)).Select(t => t.Tiquete.Criticidad));

        // Una falla no crítica no cambia el estado del equipo.
        Assert.Equal(EstadosEquipo.Operativo, (await api.LeerEquipoAsync(i.Equipo)).EstadoEquipoId);
        Assert.Empty(await api.LeerHistorialAsync(i.Equipo));

        // Con criticidad fuera de las cuatro, no hay tiquete.
        var invalida = await i.Clinico.PostAsJsonAsync(Ruta, Falla(i.Equipo, "urgente"));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Equal(3, (await api.LeerTiquetesAsync(i.Tenant)).Count);
    }

    // ---------- CA-HU-018-02 (CP-022, RN-005) ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_018_02_FallaCritica_DejaElEquipoFueraDeServicio()
    {
        var i = await InstitucionConEquipo();

        var creado = await Reportar(i.Clinico, Falla(i.Equipo, Criticidades.Critica));

        Assert.True(creado.GetProperty("equipoFueraDeServicio").GetBoolean());
        Assert.Equal("FUERA_DE_SERVICIO", creado.GetProperty("estadoEquipo").GetString());

        // El equipo y su historial de estados cambian en la misma operación que el tiquete.
        Assert.Equal(EstadosEquipo.FueraDeServicio, (await api.LeerEquipoAsync(i.Equipo)).EstadoEquipoId);
        var cambio = Assert.Single(await api.LeerHistorialAsync(i.Equipo));
        Assert.Equal(((short?)EstadosEquipo.Operativo, EstadosEquipo.FueraDeServicio, i.ClinicoId), (cambio.EstadoAnteriorId, cambio.NuevoEstadoId, cambio.UsuarioId));
        Assert.StartsWith("Falla crítica reportada (RN-005)", cambio.Motivo);

        // El ingeniero ve el equipo fuera de servicio en el inventario.
        var equipo = await Cuerpo(await i.Ingeniero.GetAsync($"/api/v1/equipos/{i.Equipo}"));
        Assert.Equal("FUERA_DE_SERVICIO", equipo.GetProperty("estado").GetString());

        // La bitácora deja el cambio del equipo ligado al tiquete.
        var evento = Assert.Single(
            await api.LeerBitacoraAsync(i.Tenant),
            e => e["entidad"].AsString == "equipo_biomedico" && e["accion"].AsString == AccionesAuditoria.Actualizar);
        Assert.Equal(("TQT-000001", "RN-005"), (evento["detalle"]["tiquete"].AsString, evento["detalle"]["regla"].AsString));

        // Otra falla crítica sobre el mismo equipo no repite el cambio de estado.
        await Reportar(i.Clinico, Falla(i.Equipo, Criticidades.Critica));
        Assert.Single(await api.LeerHistorialAsync(i.Equipo));
    }

    // ---------- Aislamiento entre instituciones (CP-036, RN-008) ----------

    [FactIntegracion]
    public async Task CP_036_OtraInstitucion_NoReportaNiVeTiquetesAjenos()
    {
        var a = await InstitucionConEquipo();
        var b = await InstitucionConEquipo();
        var codigo = (await Reportar(a.Clinico, Falla(a.Equipo, Criticidades.Critica))).GetProperty("codigo").GetString();

        // Reportar sobre un equipo de otra institución responde como si no existiera.
        var ajeno = await b.Clinico.PostAsJsonAsync(Ruta, Falla(a.Equipo, Criticidades.Critica));
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Empty(await api.LeerTiquetesAsync(b.Tenant));
        Assert.Single(await api.LeerHistorialAsync(a.Equipo));

        // El mismo código en otra institución es otro tiquete: cada una numera desde TQT-000001.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{codigo}")).StatusCode);
        Assert.Equal(0, (await Cuerpo(await b.Ingeniero.GetAsync(Ruta))).GetProperty("total").GetInt32());
        var propio = await Reportar(b.Clinico, Falla(b.Equipo, Criticidades.Baja));
        Assert.Equal("TQT-000001", propio.GetProperty("codigo").GetString());
        Assert.Equal(b.Equipo, (await Cuerpo(await b.Ingeniero.GetAsync($"{Ruta}/TQT-000001"))).GetProperty("equipoId").GetGuid());
        Assert.Equal(a.Equipo, (await Cuerpo(await a.Ingeniero.GetAsync($"{Ruta}/TQT-000001"))).GetProperty("equipoId").GetGuid());
    }

    // ---------- Permisos (API-TIQ, RNF-003) ----------

    [FactIntegracion]
    public async Task Permisos_SoloIngenieroYClinicoReportan_YElClinicoSoloVeLosSuyos()
    {
        var i = await InstitucionConEquipo();
        var otroClinico = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        var tecnico = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var delIngeniero = await Reportar(i.Ingeniero, Falla(i.Equipo, Criticidades.Baja));
        var delClinico = await Reportar(i.Clinico, Falla(i.Equipo, Criticidades.Media));

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.GerenteBiomedico, CodigosRol.DirectivaInstitucional })
        {
            var cliente = rol == CodigosRol.TecnicoMantenimiento ? tecnico : await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync(Ruta, Falla(i.Equipo, Criticidades.Alta))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync(Ruta, Falla(i.Equipo, Criticidades.Alta))).StatusCode);
        Assert.Equal(2, (await api.LeerTiquetesAsync(i.Tenant)).Count);

        // El técnico consulta todos; cada clínico, solo los que reportó.
        Assert.Equal(2, (await Cuerpo(await tecnico.GetAsync(Ruta))).GetProperty("total").GetInt32());
        var suyos = await Cuerpo(await i.Clinico.GetAsync(Ruta));
        Assert.Equal([delClinico.GetProperty("codigo").GetString()], suyos.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));
        Assert.Equal(0, (await Cuerpo(await otroClinico.GetAsync(Ruta))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await i.Clinico.GetAsync($"{Ruta}/{delIngeniero.GetProperty("codigo").GetString()}")).StatusCode);
    }

    // ---------- Apoyo ----------

    /// <summary>Institución con Ingeniero, Clínico, un tipo de falla en el catálogo y un equipo registrado con API-EQ.</summary>
    private async Task<Institucion> InstitucionConEquipo()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var ingeniero = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var correoClinico = api.Correo($"clinico-{Guid.NewGuid():N}");
        var clinicoId = await api.CrearUsuarioAsync(tenant, correoClinico, CodigosRol.PersonalClinico);
        var clinico = await api.IniciarSesionAsync(correoClinico);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);

        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.IIb, true, 12);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Urgencias");
        var registro = await ingeniero.PostAsJsonAsync("/api/v1/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = "SN-MON-01",
            ["placaInventario"] = "MON-01",
            ["marca"] = "Mindray",
            ["modelo"] = "uMEC10",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2020DM-0004321",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return new Institucion(tenant, ingeniero, clinico, clinicoId, (await Cuerpo(registro)).GetProperty("id").GetGuid());
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static object Falla(Guid equipo, string criticidad) =>
        new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "La pantalla queda en negro al conectarlo.", criticidad };

    private static async Task<JsonElement> Reportar(HttpClient cliente, object datos)
    {
        var respuesta = await cliente.PostAsJsonAsync(Ruta, datos);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.EndsWith($"/tiquetes/{cuerpo.GetProperty("codigo").GetString()}", respuesta.Headers.Location!.ToString());
        return cuerpo;
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
