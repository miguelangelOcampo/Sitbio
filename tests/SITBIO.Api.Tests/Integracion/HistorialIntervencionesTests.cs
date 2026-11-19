using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Comun;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Historial de intervenciones (HU-005) contra la API, PostgreSQL y MongoDB reales: las intervenciones se
/// registran con el flujo completo (tiquete, orden, inicio y hoja de servicio) y GET /intervenciones las
/// devuelve con el equipo, el técnico y el tiquete; filtra por equipo, tipo de equipo, técnico, tipo de
/// mantenimiento y fechas, pagina y totaliza todo el filtro. Otra institución no las ve (RN-008) y solo
/// consultan AC-01, AC-02, AC-03, AC-05 y AC-07. Cada prueba usa su propia institución.
/// </summary>
public class HistorialIntervencionesTests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/intervenciones";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);

    private sealed record Institucion(
        Guid Tenant,
        HttpClient Ingeniero,
        HttpClient Clinico,
        HttpClient Tecnico,
        Guid TecnicoId,
        Guid TipoBomba,
        Guid TipoMonitor,
        Guid Bomba,
        Guid Monitor);

    [FactIntegracion(requiereMongo: true)]
    public async Task Consulta_DevuelveLasIntervencionesConSusDatos_DeLaMasRecienteALaMasAntigua_YTotaliza()
    {
        var i = await InstitucionConEquipos();
        var tiqueteBomba = await IntervencionRegistrada(i, i.Bomba, costoManoObra: 40000, minutos: 90);
        var tiqueteMonitor = await IntervencionRegistrada(i, i.Monitor, costoManoObra: 20000, minutos: 30);

        var pagina = await Consultar(i.Ingeniero, string.Empty);

        Assert.Equal(2, pagina.GetProperty("total").GetInt32());
        var items = pagina.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["MON-01", "BOM-05"], items.Select(x => x.GetProperty("placaInventario").GetString()));
        var monitor = items[0];
        Assert.Equal("Monitor de signos vitales", monitor.GetProperty("tipoEquipo").GetString());
        Assert.Equal(i.TecnicoId, monitor.GetProperty("tecnicoId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(monitor.GetProperty("tecnico").GetString()));
        Assert.Equal(tiqueteMonitor, monitor.GetProperty("codigoTiquete").GetString());
        Assert.Equal(tiqueteBomba, items[1].GetProperty("codigoTiquete").GetString());
        Assert.Equal("correctivo", monitor.GetProperty("tipoMantenimiento").GetString());
        Assert.Equal(50000m, monitor.GetProperty("costoTotal").GetDecimal());
        Assert.False(monitor.GetProperty("cerradaFirmada").GetBoolean());

        var resumen = pagina.GetProperty("resumen");
        Assert.Equal(2, resumen.GetProperty("total").GetInt32());
        Assert.Equal(2, resumen.GetProperty("correctivas").GetInt32());
        Assert.Equal(0, resumen.GetProperty("preventivas").GetInt32());
        Assert.Equal(120000m, resumen.GetProperty("costoTotal").GetDecimal());
        Assert.Equal(60, resumen.GetProperty("tiempoPromedioMin").GetDouble());
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task Filtros_PorEquipoTipoTecnicoMantenimientoYFechas_YPaginacion()
    {
        var i = await InstitucionConEquipos();
        await IntervencionRegistrada(i, i.Bomba, costoManoObra: 40000, minutos: 90);
        await IntervencionRegistrada(i, i.Monitor, costoManoObra: 20000, minutos: 30);

        async Task<List<string?>> Placas(string consulta) =>
            (await Consultar(i.Ingeniero, consulta)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("placaInventario").GetString()).ToList();

        Assert.Equal(["BOM-05"], await Placas("equipo=bom-0"));
        Assert.Equal(["BOM-05"], await Placas("equipo=Infusomat"));
        Assert.Equal(["MON-01"], await Placas($"tipoEquipoId={i.TipoMonitor}"));
        Assert.Empty(await Placas($"equipo=BOM&tipoEquipoId={i.TipoMonitor}"));
        Assert.Equal(2, (await Placas($"tecnicoId={i.TecnicoId}")).Count);
        Assert.Empty(await Placas($"tecnicoId={Guid.NewGuid()}"));
        Assert.Equal(2, (await Placas("tipoMantenimiento=Correctivo")).Count);
        Assert.Empty(await Placas("tipoMantenimiento=preventivo"));
        Assert.Equal(2, (await Placas($"desde={Hoy:yyyy-MM-dd}&hasta={Hoy:yyyy-MM-dd}")).Count);
        Assert.Empty(await Placas($"desde={Hoy.AddDays(1):yyyy-MM-dd}"));
        Assert.Empty(await Placas($"hasta={Hoy.AddDays(-1):yyyy-MM-dd}"));

        var segunda = await Consultar(i.Ingeniero, "page=2&pageSize=1");
        Assert.Equal(2, segunda.GetProperty("total").GetInt32());
        Assert.Equal(["BOM-05"], segunda.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("placaInventario").GetString()));

        // Los totales son de todo el filtro, no de la página.
        Assert.Equal(1, (await Consultar(i.Ingeniero, "equipo=MON&pageSize=1")).GetProperty("resumen").GetProperty("total").GetInt32());
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task OtraInstitucion_NoVeLasIntervenciones_Rn008()
    {
        var i = await InstitucionConEquipos();
        await IntervencionRegistrada(i, i.Bomba, costoManoObra: 40000, minutos: 90);
        var otra = await InstitucionConEquipos();

        var pagina = await Consultar(otra.Ingeniero, string.Empty);
        Assert.Equal(0, pagina.GetProperty("total").GetInt32());
        Assert.Equal(0, pagina.GetProperty("resumen").GetProperty("total").GetInt32());
        Assert.Equal(0, (await Consultar(otra.Ingeniero, $"tecnicoId={i.TecnicoId}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Consultar(i.Ingeniero, string.Empty)).GetProperty("total").GetInt32());
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task Roles_ConsultanAc01Ac02Ac03Ac05Ac07_LosDemasReciben403_SinToken401()
    {
        var i = await InstitucionConEquipos();
        await IntervencionRegistrada(i, i.Bomba, costoManoObra: 40000, minutos: 90);

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.GerenteBiomedico, CodigosRol.EnteRegulador })
        {
            var (cliente, _) = await SesionDe(i.Tenant, rol);
            Assert.Equal(1, (await Consultar(cliente, string.Empty)).GetProperty("total").GetInt32());
        }

        foreach (var rol in new[] { CodigosRol.PersonalClinico, CodigosRol.DirectivaInstitucional })
        {
            var (cliente, _) = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Ruta)).StatusCode);
    }

    [FactIntegracion(requiereMongo: true)]
    public async Task FiltrosInvalidos_Responden400ConElCampo()
    {
        var i = await InstitucionConEquipos();

        async Task<string> CampoConError(string consulta)
        {
            var respuesta = await i.Ingeniero.GetAsync($"{Ruta}?{consulta}");
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            return (await Cuerpo(respuesta)).GetProperty("errors").EnumerateObject().Single().Name;
        }

        Assert.Equal("tipoMantenimiento", await CampoConError("tipoMantenimiento=calibracion"));
        Assert.Equal("hasta", await CampoConError("desde=2026-11-10&hasta=2026-11-01"));
        Assert.Equal("pageSize", await CampoConError("pageSize=0"));
        Assert.Equal("page", await CampoConError("page=0"));
    }

    private async Task<JsonElement> Consultar(HttpClient cliente, string consulta)
    {
        var respuesta = await cliente.GetAsync(string.IsNullOrEmpty(consulta) ? Ruta : $"{Ruta}?{consulta}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    /// <summary>Tiquete, orden asignada, inicio y hoja de servicio; devuelve el código del tiquete.</summary>
    private static async Task<string> IntervencionRegistrada(Institucion i, Guid equipo, decimal costoManoObra, int minutos)
    {
        var reporte = await i.Clinico.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarlo.", criticidad = Criticidades.Alta });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var tiquete = await Cuerpo(reporte);
        var asignada = await i.Ingeniero.PostAsJsonAsync("/api/v1/ordenes", new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        var orden = (await Cuerpo(asignada)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.PatchAsJsonAsync($"/api/v1/ordenes/{orden}/estado", new { accion = "iniciar" })).StatusCode);
        var hoja = await i.Tecnico.PutAsJsonAsync($"/api/v1/ordenes/{orden}/intervencion", new
        {
            diagnostico = "Fuente de poder averiada",
            accionesRealizadas = "Se reemplazó la fuente y se verificó el funcionamiento",
            repuestos = new[] { new { nombre = "Fuente de poder", cantidad = 2, costoUnitario = 15000 } },
            costoManoObra,
            tiempoAtencionMin = minutos,
            estadoEquipoSalida = EstadosSalidaEquipo.Operativo,
        });
        Assert.Equal(HttpStatusCode.OK, hoja.StatusCode);
        return tiquete.GetProperty("codigo").GetString()!;
    }

    private async Task<Institucion> InstitucionConEquipos()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, _) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (clinico, _) = await SesionDe(tenant, CodigosRol.PersonalClinico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);

        var tipoBomba = await api.CrearTipoEquipoAsync(tenant, "Bomba de infusión", ClasesRiesgo.IIb, false, null);
        var tipoMonitor = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.IIb, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Hospitalización");
        var bomba = await Equipo(ingeniero, tipoBomba, ubicacion, "BOM-05", "B. Braun", "Infusomat Space");
        var monitor = await Equipo(ingeniero, tipoMonitor, ubicacion, "MON-01", "Mindray", "BeneView T5");
        return new Institucion(tenant, ingeniero, clinico, tecnico, tecnicoId, tipoBomba, tipoMonitor, bomba, monitor);
    }

    private static async Task<Guid> Equipo(HttpClient ingeniero, Guid tipo, Guid ubicacion, string placa, string marca, string modelo)
    {
        var registro = await ingeniero.PostAsJsonAsync("/api/v1/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = marca,
            ["modelo"] = modelo,
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2021DM-0007788",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = false,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return (await Cuerpo(registro)).GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"hist-{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
