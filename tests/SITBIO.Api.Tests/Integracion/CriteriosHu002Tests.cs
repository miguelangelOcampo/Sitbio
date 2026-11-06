using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-002 (DOC-04) contra la API y PostgreSQL reales: el Ingeniero
/// Biomédico (AC-02) inicia sesión de verdad y el resultado se comprueba en la base. Cada prueba
/// usa su propia institución con el plan fijado de forma explícita (RN-009).
/// </summary>
public class CriteriosHu002Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/equipos";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, Guid Ubicacion, Guid Tensiometro, Guid Estetoscopio);

    /// <summary>
    /// Institución con una ubicación, dos tipos de equipo y un Ingeniero Biomédico con sesión. El
    /// tensiómetro (clase I) se calibra cada 12 meses; el estetoscopio (clase I) no se calibra (RN-010).
    /// </summary>
    private async Task<Institucion> InstitucionConIngeniero(string plan = "Clínica", int maxEquipos = 1000)
    {
        var tenant = await api.CrearTenantAsync(plan, 7, maxEquipos, 25);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Urgencias");
        var tensiometro = await api.CrearTipoEquipoAsync(tenant, "Tensiómetro", ClasesRiesgo.I, true, 12);
        var estetoscopio = await api.CrearTipoEquipoAsync(tenant, "Estetoscopio", ClasesRiesgo.I, false, null);
        var correo = api.Correo($"ingeniero-{tenant:N}");
        await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        return new Institucion(tenant, await api.IniciarSesionAsync(correo), ubicacion, tensiometro, estetoscopio);
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    /// <summary>Datos completos de un tensiómetro (RN-010); cada prueba quita o cambia lo que necesita.</summary>
    private static Dictionary<string, object?> Datos(Institucion i, string placa, DateOnly? vence = null) => new()
    {
        ["tipoEquipoId"] = i.Tensiometro,
        ["ubicacionId"] = i.Ubicacion,
        ["numeroSerie"] = $"SN-{placa}",
        ["placaInventario"] = placa,
        ["marca"] = "Welch Allyn",
        ["modelo"] = "DS44",
        ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
        ["registroInvima"] = "INVIMA 2019DM-0001234",
        ["venceRegistro"] = vence ?? Hoy.AddYears(2),
        ["claseRiesgo"] = ClasesRiesgo.I,
        ["requiereCalibracion"] = true,
        ["periodicidadCalibracionMeses"] = 12,
    };

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<Guid> Registrar(Institucion i, Dictionary<string, object?> datos)
    {
        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return Guid.Parse((await Cuerpo(respuesta)).GetProperty("id").GetString()!);
    }

    private static string[] CamposConError(JsonElement cuerpo) =>
        cuerpo.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    // ---------- CA-HU-002-01 ----------

    [FactIntegracion]
    public async Task CA_HU_002_01_ConLosDatosObligatorios_ElEquipoQuedaOperativoEnPostgres()
    {
        var i = await InstitucionConIngeniero();

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, Datos(i, "HSJ-0001"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        var id = Guid.Parse(cuerpo.GetProperty("id").GetString()!);
        Assert.EndsWith($"/equipos/{id}", respuesta.Headers.Location?.ToString());
        Assert.Equal(EstadosEquipo.CodigoOperativo, cuerpo.GetProperty("estado").GetString());

        var guardado = await api.LeerEquipoAsync(id);
        Assert.Equal(i.Tenant, guardado.TenantId);
        Assert.Equal(EstadosEquipo.Operativo, guardado.EstadoEquipoId);
        Assert.Equal("HSJ-0001", guardado.PlacaInventario);
        Assert.Equal("SN-HSJ-0001", guardado.NumeroSerie);
        Assert.Null(guardado.FechaBaja);

        var consulta = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/{id}"));
        Assert.Equal("Tensiómetro", consulta.GetProperty("tipoEquipo").GetString());
        Assert.Equal("Urgencias", consulta.GetProperty("ubicacion").GetString());
        Assert.Equal("Operativo", consulta.GetProperty("estadoNombre").GetString());
    }

    [FactIntegracion]
    public async Task CA_HU_002_01_SinUnDatoObligatorio_Responde400EnEseCampoYNoGuarda()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "HSJ-0002");
        datos.Remove("placaInventario");
        datos["marca"] = " ";
        datos.Remove("ubicacionId");

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(["marca", "placaInventario", "ubicacionId"], CamposConError(await Cuerpo(respuesta)));
        Assert.Equal(0, await api.ContarEquiposAsync(i.Tenant));
    }

    // ---------- CA-HU-002-02 ----------

    [FactIntegracion]
    public async Task CA_HU_002_02_PlacaOSerieRepetidaEnLaInstitucion_Responde409Rn001()
    {
        var i = await InstitucionConIngeniero();
        await Registrar(i, Datos(i, "HSJ-0010"));

        var otraSerie = Datos(i, "HSJ-0010");
        otraSerie["numeroSerie"] = "SN-DIFERENTE";
        var otraPlaca = Datos(i, "HSJ-0011");
        otraPlaca["numeroSerie"] = "SN-HSJ-0010";

        foreach (var (datos, dato) in new[] { (otraSerie, "placa de inventario"), (otraPlaca, "número de serie") })
        {
            var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);

            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            var cuerpo = await Cuerpo(respuesta);
            Assert.Equal(GestionEquipos.CodigoDuplicado, cuerpo.GetProperty("codigo").GetString());
            Assert.Contains(dato, cuerpo.GetProperty("detail").GetString());
        }

        Assert.Equal(1, await api.ContarEquiposAsync(i.Tenant));
    }

    [FactIntegracion]
    public async Task CA_HU_002_02_EditarConLaPlacaDeOtroEquipo_Responde409YNoCambia()
    {
        var i = await InstitucionConIngeniero();
        await Registrar(i, Datos(i, "HSJ-0020"));
        var segundo = await Registrar(i, Datos(i, "HSJ-0021"));
        var datos = Datos(i, "HSJ-0020");
        datos["numeroSerie"] = "SN-HSJ-0021";

        var respuesta = await i.Ingeniero.PutAsJsonAsync($"{Ruta}/{segundo}", datos);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("HSJ-0021", (await api.LeerEquipoAsync(segundo)).PlacaInventario);
    }

    [FactIntegracion]
    public async Task CA_HU_002_02_LaMismaPlacaEnOtraInstitucion_SeAcepta()
    {
        var a = await InstitucionConIngeniero();
        var b = await InstitucionConIngeniero();
        await Registrar(a, Datos(a, "HSJ-0030"));

        var respuesta = await b.Ingeniero.PostAsJsonAsync(Ruta, Datos(b, "HSJ-0030"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    // ---------- CA-HU-002-03 ----------

    [FactIntegracion]
    public async Task CA_HU_002_03_PlanConsultorioLleno_ImpideRegistrarYConservaConsultaYEdicion()
    {
        // Consultorio: 300 equipos activos (DOC-01 Tabla 4).
        var i = await InstitucionConIngeniero("Consultorio", 300);
        await api.LlenarEquiposAsync(i.Tenant, i.Estetoscopio, i.Ubicacion, 299, "LL");
        var ultimo = await Registrar(i, Datos(i, "HSJ-0300"));

        var rechazo = await i.Ingeniero.PostAsJsonAsync(Ruta, Datos(i, "HSJ-0301"));

        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        var cuerpo = await Cuerpo(rechazo);
        Assert.Equal(GestionEquipos.CodigoLimitePlan, cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("plan Consultorio (300 de 300)", cuerpo.GetProperty("detail").GetString());
        Assert.Equal(300, await api.ContarEquiposAsync(i.Tenant));

        // RN-009: la consulta y la edición de los equipos existentes siguen funcionando.
        var lista = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}?pageSize=5"));
        Assert.Equal(300, lista.GetProperty("total").GetInt32());
        var edicion = Datos(i, "HSJ-0300");
        edicion["marca"] = "Omron";
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PutAsJsonAsync($"{Ruta}/{ultimo}", edicion)).StatusCode);
        Assert.Equal("Omron", (await api.LeerEquipoAsync(ultimo)).Marca);
    }

    [FactIntegracion]
    public async Task CA_HU_002_03_UnEquipoDadoDeBajaLiberaCupo()
    {
        var i = await InstitucionConIngeniero("Consultorio", 300);
        await api.LlenarEquiposAsync(i.Tenant, i.Estetoscopio, i.Ubicacion, 299, "BJ");
        var ultimo = await Registrar(i, Datos(i, "HSJ-0400"));
        Assert.Equal(HttpStatusCode.Conflict, (await i.Ingeniero.PostAsJsonAsync(Ruta, Datos(i, "HSJ-0401"))).StatusCode);

        await api.DarDeBajaAsync(ultimo);

        Assert.Equal(HttpStatusCode.Created, (await i.Ingeniero.PostAsJsonAsync(Ruta, Datos(i, "HSJ-0401"))).StatusCode);
    }

    // ---------- CA-HU-002-04 ----------

    [FactIntegracion]
    public async Task CA_HU_002_04_SinNumeroNiVigenciaDeLaAutorizacion_Responde400()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "HSJ-0500");
        datos.Remove("registroInvima");
        datos.Remove("venceRegistro");
        datos["tipoAutorizacion"] = "licencia";
        datos["claseRiesgo"] = "IV";

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(["claseRiesgo", "registroInvima", "tipoAutorizacion", "venceRegistro"], CamposConError(await Cuerpo(respuesta)));
        Assert.Equal(0, await api.ContarEquiposAsync(i.Tenant));
    }

    [FactIntegracion]
    public async Task CA_HU_002_04_ConCalibracion_LaPeriodicidadVaDe1A60Meses()
    {
        var i = await InstitucionConIngeniero();

        foreach (var meses in new int?[] { 0, 61, null })
        {
            var datos = Datos(i, $"HSJ-05{meses ?? 99}");
            datos["periodicidadCalibracionMeses"] = meses;

            var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal(["periodicidadCalibracionMeses"], CamposConError(await Cuerpo(respuesta)));
        }

        var valido = Datos(i, "HSJ-0560");
        valido["periodicidadCalibracionMeses"] = 60;
        var id = await Registrar(i, valido);
        Assert.Equal((short)60, (await api.LeerEquipoAsync(id)).PeriodicidadCalibracionMeses);
    }

    [FactIntegracion]
    public async Task CA_HU_002_04_EstetoscopioClaseISinCalibracion_SeGuardaSinPeriodicidad()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "HSJ-0570");
        datos["tipoEquipoId"] = i.Estetoscopio;
        datos["requiereCalibracion"] = false;

        var id = await Registrar(i, datos);

        var guardado = await api.LeerEquipoAsync(id);
        Assert.Equal(ClasesRiesgo.I, guardado.ClaseRiesgo);
        Assert.False(guardado.RequiereCalibracion);
        Assert.Null(guardado.PeriodicidadCalibracionMeses);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_002_04_SiElIngenieroAjustaLosValoresDelTipo_QuedaEnLaBitacora()
    {
        var i = await InstitucionConIngeniero();
        var igual = await Registrar(i, Datos(i, "HSJ-0580"));
        var ajustado = Datos(i, "HSJ-0581");
        ajustado["claseRiesgo"] = ClasesRiesgo.IIa;
        ajustado["periodicidadCalibracionMeses"] = 6;
        var distinto = await Registrar(i, ajustado);

        var eventos = (await api.LeerBitacoraAsync(i.Tenant)).Where(d => d["accion"] == "CREAR" && d["entidad"] == GestionEquipos.Entidad).ToList();

        var sinCambios = Assert.Single(eventos, d => d["entidad_id"] == igual.ToString());
        Assert.False(sinCambios["detalle"].AsBsonDocument.Contains("difiere_del_tipo"));
        var conCambios = Assert.Single(eventos, d => d["entidad_id"] == distinto.ToString());
        Assert.Equal("HSJ-0581", conCambios["detalle"]["placa"].AsString);
        Assert.Equal("clase_riesgo,periodicidad_calibracion_meses", conCambios["detalle"]["difiere_del_tipo"].AsString);
    }

    // ---------- CA-HU-002-05 ----------

    [FactIntegracion]
    public async Task CA_HU_002_05_FiltrosPorClaseYCalibracion_DevuelvenSoloLosQueCumplen()
    {
        var i = await InstitucionConIngeniero();
        await Registrar(i, Datos(i, "F-TENS"));
        var monitor = Datos(i, "F-MONI");
        monitor["claseRiesgo"] = ClasesRiesgo.IIb;
        await Registrar(i, monitor);
        var estetoscopio = Datos(i, "F-ESTE");
        estetoscopio["tipoEquipoId"] = i.Estetoscopio;
        estetoscopio["requiereCalibracion"] = false;
        await Registrar(i, estetoscopio);

        async Task<string[]> Placas(string consulta)
        {
            var cuerpo = await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}?{consulta}"));
            return cuerpo.GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("placaInventario").GetString()!)
                .Order(StringComparer.Ordinal).ToArray();
        }

        Assert.Equal(["F-ESTE", "F-TENS"], await Placas("clase=I"));
        Assert.Equal(["F-MONI"], await Placas("clase=IIb"));
        Assert.Equal(["F-MONI", "F-TENS"], await Placas("calibracion=true"));
        Assert.Equal(["F-ESTE"], await Placas("calibracion=false"));
        Assert.Equal(["F-TENS"], await Placas("clase=I&calibracion=true"));
        Assert.Equal(HttpStatusCode.BadRequest, (await i.Ingeniero.GetAsync($"{Ruta}?clase=IV")).StatusCode);
    }

    [FactIntegracion]
    public async Task CA_HU_002_05_LaHojaDeVidaTraeLaInformacionRegulatoriaYElAvisoDelRegistro()
    {
        var i = await InstitucionConIngeniero();
        var vencido = await Registrar(i, Datos(i, "A-VENC", Hoy.AddDays(-1)));
        var porVencer = await Registrar(i, Datos(i, "A-PORV", Hoy.AddDays(Calibracion.DiasAvisoRegistro)));
        var vigente = await Registrar(i, Datos(i, "A-VIGE", Hoy.AddDays(Calibracion.DiasAvisoRegistro + 1)));
        var tecnico = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);

        foreach (var (id, aviso) in new[] { (vencido, AvisoRegistro.Vencido), (porVencer, AvisoRegistro.PorVencer), (vigente, AvisoRegistro.Vigente) })
        {
            var hoja = await Cuerpo(await tecnico.GetAsync($"{Ruta}/{id}"));

            Assert.Equal(aviso.ToString(), hoja.GetProperty("avisoRegistro").GetString());
            Assert.Equal(TiposAutorizacion.RegistroSanitario, hoja.GetProperty("tipoAutorizacion").GetString());
            Assert.Equal("INVIMA 2019DM-0001234", hoja.GetProperty("registroInvima").GetString());
            Assert.Equal(ClasesRiesgo.I, hoja.GetProperty("claseRiesgo").GetString());
            Assert.Equal(12, hoja.GetProperty("periodicidadCalibracionMeses").GetInt32());
        }
    }

    // ---------- CA-HU-002-06 ----------

    [FactIntegracion]
    public async Task CA_HU_002_06_ConElUltimoPreventivo_CalculaElProximoYCreaElPlanActivo()
    {
        var i = await InstitucionConIngeniero();
        var ultima = Hoy.AddDays(-30);
        var datos = Datos(i, "P-0001");
        datos["preventivo"] = new { periodicidadDias = 180, ultimaEjecucion = ultima };

        var id = await Registrar(i, datos);

        var plan = Assert.Single(await api.LeerPlanesAsync(id));
        Assert.Equal(PlanPreventivo.Activo, plan.Estado);
        Assert.Equal(ultima, plan.UltimaEjecucion);
        Assert.Equal(ultima.AddDays(180), plan.ProximaFecha);
        Assert.Equal(180, plan.PeriodicidadDias);
        Assert.Equal(i.Tenant, plan.TenantId);
    }

    [FactIntegracion]
    public async Task CA_HU_002_06_SiElProximoCalculadoYaPaso_ElPlanNaceVencido()
    {
        var i = await InstitucionConIngeniero();
        var ultima = Hoy.AddDays(-200);
        var datos = Datos(i, "P-0002");
        datos["preventivo"] = new { periodicidadDias = 180, ultimaEjecucion = ultima };

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, datos);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.True(cuerpo.GetProperty("preventivo").GetProperty("vencido").GetBoolean());
        var plan = Assert.Single(await api.LeerPlanesAsync(Guid.Parse(cuerpo.GetProperty("id").GetString()!)));
        Assert.Equal(ultima.AddDays(180), plan.ProximaFecha);
        Assert.True(plan.EstaVencido(Hoy));
    }

    [FactIntegracion]
    public async Task CA_HU_002_06_ElProximoEsAjustable()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "P-0003");
        datos["preventivo"] = new { periodicidadDias = 180, ultimaEjecucion = Hoy.AddDays(-30), proximaFecha = Hoy.AddDays(7) };

        var id = await Registrar(i, datos);

        Assert.Equal(Hoy.AddDays(7), Assert.Single(await api.LeerPlanesAsync(id)).ProximaFecha);
    }

    [FactIntegracion]
    public async Task CA_HU_002_06_SinElUltimo_SeIndicaElProximoQueNoPuedeHaberPasado()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "P-0004");
        datos["preventivo"] = new { periodicidadDias = 90, proximaFecha = Hoy.AddDays(15) };
        var id = await Registrar(i, datos);
        var plan = Assert.Single(await api.LeerPlanesAsync(id));
        Assert.Null(plan.UltimaEjecucion);
        Assert.Equal(Hoy.AddDays(15), plan.ProximaFecha);

        foreach (var (preventivo, placa) in new object[] { new { periodicidadDias = 90 }, new { periodicidadDias = 90, proximaFecha = Hoy.AddDays(-1) } }
                     .Select((p, n) => (p, $"P-00{5 + n}")))
        {
            var invalido = Datos(i, placa);
            invalido["preventivo"] = preventivo;

            var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, invalido);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal(["preventivo.proximaFecha"], CamposConError(await Cuerpo(respuesta)));
        }

        Assert.Equal(1, await api.ContarEquiposAsync(i.Tenant));
    }

    [FactIntegracion]
    public async Task CA_HU_002_06_AlEditar_SeReprogramaElMismoPlan()
    {
        var i = await InstitucionConIngeniero();
        var datos = Datos(i, "P-0010");
        datos["preventivo"] = new { periodicidadDias = 180, ultimaEjecucion = Hoy.AddDays(-30) };
        var id = await Registrar(i, datos);
        var original = Assert.Single(await api.LeerPlanesAsync(id));

        datos["preventivo"] = new { periodicidadDias = 90, ultimaEjecucion = Hoy.AddDays(-10) };
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PutAsJsonAsync($"{Ruta}/{id}", datos)).StatusCode);

        var plan = Assert.Single(await api.LeerPlanesAsync(id));
        Assert.Equal(original.Id, plan.Id);
        Assert.Equal(Hoy.AddDays(80), plan.ProximaFecha);
    }

    // ---------- Permisos (RNF-003) y aislamiento (RN-008) ----------

    [FactIntegracion]
    public async Task Permisos_ElTecnicoConsultaPeroNoRegistraYLaDirectivaNoConsulta()
    {
        var i = await InstitucionConIngeniero();
        var tecnico = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var directiva = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);

        Assert.Equal(HttpStatusCode.OK, (await tecnico.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tecnico.PostAsJsonAsync(Ruta, Datos(i, "X-0001"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await directiva.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Ruta)).StatusCode);
        Assert.Equal(0, await api.ContarEquiposAsync(i.Tenant));
    }

    [FactIntegracion]
    public async Task Aislamiento_OtraInstitucionNoVeNiEditaElEquipo()
    {
        var a = await InstitucionConIngeniero();
        var b = await InstitucionConIngeniero();
        var id = await Registrar(a, Datos(a, "AIS-0001"));

        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.PutAsJsonAsync($"{Ruta}/{id}", Datos(b, "AIS-0001"))).StatusCode);
        Assert.Equal(0, (await Cuerpo(await b.Ingeniero.GetAsync($"{Ruta}?placa=AIS"))).GetProperty("total").GetInt32());
        Assert.Equal("Welch Allyn", (await api.LeerEquipoAsync(id)).Marca);
        Assert.Equal(a.Tenant, (await api.LeerEquipoAsync(id)).TenantId);
    }
}
