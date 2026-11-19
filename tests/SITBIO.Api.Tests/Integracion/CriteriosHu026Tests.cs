using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de HU-026 contra la API, PostgreSQL y MongoDB reales: los cinco indicadores se calculan sobre los
/// datos de la institución (CA-HU-026-01); la Directiva solo consulta el tablero (CA-HU-026-02).
/// </summary>
public class CriteriosHu026Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Indicadores = "/api/v1/indicadores";
    private const string Ordenes = "/api/v1/ordenes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, HttpClient Tecnico, Guid TecnicoId, HttpClient Clinico, Guid Tipo, Guid Ubicacion);

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_026_01_LosCincoIndicadores_SeCalculanSobreLosDatosDeLaInstitucion()
    {
        var i = await NuevaInstitucion();
        var averiado = await Equipo(i, "MON-81");
        await Equipo(i, "MON-82");
        await Equipo(i, "MON-83");
        await Equipo(i, "MON-84");
        var reparada = await Orden(i, await Equipo(i, "MON-85"), Criticidades.Alta);
        await Orden(i, averiado, Criticidades.Critica);
        await Atender(i, reparada);

        // Otra institución con sus propias fallas no cambia los números.
        var otra = await NuevaInstitucion();
        await Orden(otra, await Equipo(otra, "OTR-81"), Criticidades.Critica);

        var (gerente, _) = await SesionDe(i.Tenant, CodigosRol.GerenteBiomedico);
        var kpi = await Cuerpo(await gerente.GetAsync(Indicadores));

        Assert.True(kpi.GetProperty("costosCompletos").GetBoolean());
        var fuera = kpi.GetProperty("fueraDeServicio");
        Assert.Equal((1, 5, 20.0), (fuera.GetProperty("equipos").GetInt32(), fuera.GetProperty("equiposActivos").GetInt32(), fuera.GetProperty("porcentaje").GetDouble()));
        var costo = kpi.GetProperty("costoPorEquipo");
        Assert.Equal((55000m, 11000m), (costo.GetProperty("costoTotal").GetDecimal(), costo.GetProperty("promedio").GetDecimal()));
        Assert.True(kpi.GetProperty("mttrHoras").GetDouble() >= 0);
        Assert.Equal(Math.Round(30 * 24 * 5 / 2.0, 1), kpi.GetProperty("mtbfHoras").GetDouble());
        Assert.Equal(0, kpi.GetProperty("cumplimientoPreventivo").GetProperty("programadas").GetInt32());
        Assert.Equal(JsonValueKind.Null, kpi.GetProperty("cumplimientoPreventivo").GetProperty("porcentaje").ValueKind);
    }

    [FactIntegracion]
    public async Task CA_HU_026_02_Directiva_SoloConsultaElTablero()
    {
        var i = await NuevaInstitucion();
        var (directiva, _) = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);

        Assert.Equal(HttpStatusCode.OK, (await directiva.GetAsync(Indicadores)).StatusCode);
        foreach (var ruta in new[] { "/api/v1/equipos", Ordenes, "/api/v1/tiquetes", "/api/v1/calibraciones", "/api/v1/usuarios" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await directiva.GetAsync(ruta)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync(Indicadores)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await directiva.GetAsync($"{Indicadores}?desde=2026-11-30&hasta=2026-11-01")).StatusCode);
    }

    /// <summary>I-02: la serie mensual de los gráficos solo cuenta las órdenes de la institución del token.</summary>
    [FactIntegracion(requiereMongo: true)]
    public async Task SerieMensual_CuentaSoloLasOrdenesDeLaInstitucion()
    {
        var i = await NuevaInstitucion();
        await Atender(i, await Orden(i, await Equipo(i, "SER-81"), Criticidades.Alta));
        var otra = await NuevaInstitucion();
        await Atender(otra, await Orden(otra, await Equipo(otra, "SER-91"), Criticidades.Alta));
        await Atender(otra, await Orden(otra, await Equipo(otra, "SER-92"), Criticidades.Media));

        var (directiva, _) = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);
        var serie = await Cuerpo(await directiva.GetAsync($"{Indicadores}/series?meses=3"));

        Assert.Equal(3, serie.GetArrayLength());
        var actual = serie[2];
        Assert.Equal(new DateOnly(Hoy.Year, Hoy.Month, 1), DateOnly.Parse(actual.GetProperty("mes").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, actual.GetProperty("correctivos").GetInt32());
        Assert.Equal(0, serie[0].GetProperty("correctivos").GetInt32() + serie[1].GetProperty("correctivos").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync($"{Indicadores}/series")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await directiva.GetAsync($"{Indicadores}/series?meses=13")).StatusCode);
    }

    /// <summary>I-02: el estado del parque y los equipos fuera de servicio son solo los de la institución.</summary>
    [FactIntegracion]
    public async Task Parque_CuentaLosEquiposPorEstado_SoloDeLaInstitucion()
    {
        var i = await NuevaInstitucion();
        await Equipo(i, "PAR-81");
        await Orden(i, await Equipo(i, "PAR-82"), Criticidades.Critica);
        var otra = await NuevaInstitucion();
        await Orden(otra, await Equipo(otra, "PAR-91"), Criticidades.Critica);

        var (directiva, _) = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);
        var parque = await Cuerpo(await directiva.GetAsync($"{Indicadores}/parque"));

        Assert.Equal(1, parque.GetProperty("operativos").GetInt32());
        Assert.Equal(1, parque.GetProperty("fueraDeServicio").GetInt32());
        var fuera = Assert.Single(parque.GetProperty("equiposFuera").EnumerateArray());
        Assert.Equal("PAR-82", fuera.GetProperty("placaInventario").GetString());
        Assert.Equal("UCI adultos", fuera.GetProperty("ubicacion").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync($"{Indicadores}/parque")).StatusCode);
    }

    // ---------- Apoyo ----------

    private async Task<Guid> Orden(Institucion i, Guid equipo, string criticidad)
    {
        var reporte = await i.Clinico.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarlo.", criticidad });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ordenes, new { tiqueteId = (await Cuerpo(reporte)).GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        return (await Cuerpo(asignada)).GetProperty("id").GetGuid();
    }

    private static async Task Atender(Institucion i, Guid orden)
    {
        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.PatchAsJsonAsync($"{Ordenes}/{orden}/estado", new { accion = AccionesOrden.Iniciar })).StatusCode);
        var hoja = new
        {
            diagnostico = "Fuente de poder averiada",
            accionesRealizadas = "Se cambió la fuente y se probó el equipo",
            repuestos = new[] { new { nombre = "Fuente de poder", cantidad = 1, costoUnitario = 15000 } },
            costoManoObra = 40000,
            tiempoAtencionMin = 90,
            estadoEquipoSalida = EstadosSalidaEquipo.Operativo,
        };
        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.PutAsJsonAsync($"{Ordenes}/{orden}/intervencion", hoja)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.PatchAsJsonAsync($"{Ordenes}/{orden}/estado", new { accion = AccionesOrden.Cerrar })).StatusCode);
    }

    private static async Task<Guid> Equipo(Institucion i, string placa)
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
            ["claseRiesgo"] = ClasesRiesgo.I,
            ["requiereCalibracion"] = false,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return (await Cuerpo(registro)).GetProperty("id").GetGuid();
    }

    private async Task<Institucion> NuevaInstitucion()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, _) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        var (clinico, _) = await SesionDe(tenant, CodigosRol.PersonalClinico);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.I, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        return new Institucion(tenant, ingeniero, tecnico, tecnicoId, clinico, tipo, ubicacion);
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
