using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Equipos;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-023 (CP-027) contra la API, PostgreSQL y MongoDB reales: la línea de tiempo del
/// equipo une registro, estados, tiquetes, órdenes, intervenciones y calibraciones en orden cronológico
/// (CA-HU-023-01); el Ente Regulador la consulta sin poder editar nada (CA-HU-023-02). Cada prueba usa su
/// propia institución.
/// </summary>
public class CriteriosHu023Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Equipos = "/api/v1/equipos";
    private const string Ordenes = "/api/v1/ordenes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, HttpClient Tecnico, Guid TecnicoId, Guid Tipo, Guid Ubicacion);

    // ---------- CA-HU-023-01: todo el ciclo de vida, en orden cronológico ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_023_01_LineaDeTiempo_UneElCicloDeVidaEnOrdenCronologico()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "MON-23");
        var calibracion = await i.Tecnico.PostAsJsonAsync("/api/v1/calibraciones", new { equipoId = equipo, fechaCalibracion = Hoy.AddDays(-3), patronReferencia = "Analizador Fluke IDA-5" });
        Assert.Equal(HttpStatusCode.Created, calibracion.StatusCode);
        var orden = await OrdenAtendida(i, equipo);
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.PostAsJsonAsync($"{Ordenes}/{orden}/aprobacion", new { firma = "Laura Gómez, Ingeniera Biomédica" })).StatusCode);

        var linea = await Cuerpo(await i.Ingeniero.GetAsync($"{Equipos}/{equipo}/linea-tiempo"));

        Assert.Equal(("MON-23", true), (Texto(linea, "placaInventario"), linea.GetProperty("intervencionesCompletas").GetBoolean()));
        var eventos = linea.GetProperty("eventos").EnumerateArray().ToList();
        var fechas = eventos.Select(e => e.GetProperty("fecha").GetDateTimeOffset()).ToList();
        Assert.Equal(fechas.Order(), fechas);

        // La calibración se fechó tres días antes del registro en SITBIO: va primero, por su fecha.
        Assert.Equal([TiposEvento.Calibracion, TiposEvento.Registro], eventos.Take(2).Select(e => Texto(e, "tipo")));
        Assert.Equivalent(
            new[] { TiposEvento.Registro, TiposEvento.Estado, TiposEvento.Tiquete, TiposEvento.Orden, TiposEvento.Intervencion, TiposEvento.Calibracion },
            eventos.Select(e => Texto(e, "tipo")).Distinct().ToArray());

        // La falla crítica deja el equipo fuera de servicio; la orden aprobada lo devuelve a Operativo.
        Assert.Contains(eventos, e => Texto(e, "titulo") == "Orden correctiva aprobada y firmada" && Texto(e, "responsable") is not null);
        Assert.Contains(eventos, e => Texto(e, "titulo") == "Intervención registrada · sale «Operativo»");
        var estados = eventos.Where(e => Texto(e, "tipo") == TiposEvento.Estado).Select(e => Texto(e, "titulo")).ToList();
        Assert.Equal(2, estados.Count);
        Assert.EndsWith("Operativo", estados[^1]);
    }

    // ---------- CA-HU-023-02: el Ente Regulador consulta, no edita ----------

    [FactIntegracion]
    public async Task CA_HU_023_02_EnteRegulador_ConsultaLaLineaDeTiempo_PeroNoEdita()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "MON-24");
        var (auditor, _) = await SesionDe(i.Tenant, CodigosRol.EnteRegulador);

        var respuesta = await auditor.GetAsync($"{Equipos}/{equipo}/linea-tiempo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(TiposEvento.Registro, Texto((await Cuerpo(respuesta)).GetProperty("eventos")[0], "tipo"));
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PutAsJsonAsync($"{Equipos}/{equipo}", new { placaInventario = "OTRA" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsJsonAsync("/api/v1/calibraciones", new { equipoId = equipo, fechaCalibracion = Hoy, patronReferencia = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "X", criticidad = Criticidades.Baja })).StatusCode);
    }

    // ---------- CP-036: aislamiento entre instituciones ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoVeLaLineaDeTiempoAjena()
    {
        var a = await NuevaInstitucion();
        var b = await NuevaInstitucion();
        var equipoA = await Equipo(a, "MON-25");

        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Equipos}/{equipoA}/linea-tiempo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Ingeniero.GetAsync($"{Equipos}/{Guid.NewGuid()}/linea-tiempo")).StatusCode);
    }

    // ---------- Apoyo ----------

    /// <summary>Falla crítica (el equipo sale de servicio), orden asignada, iniciada, con intervención y cerrada.</summary>
    private async Task<Guid> OrdenAtendida(Institucion i, Guid equipo)
    {
        var (clinico, _) = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        await api.CrearTipoFallaAsync(i.Tenant, FallaNoEnciende);
        var reporte = await clinico.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarlo.", criticidad = Criticidades.Critica });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ordenes, new { tiqueteId = (await Cuerpo(reporte)).GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        var orden = (await Cuerpo(asignada)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Iniciar)).StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, (await Estado(i.Tecnico, orden, AccionesOrden.Cerrar)).StatusCode);
        return orden;
    }

    private static Task<HttpResponseMessage> Estado(HttpClient cliente, Guid orden, string accion) =>
        cliente.PatchAsJsonAsync($"{Ordenes}/{orden}/estado", new { accion });

    /// <summary>Equipo clase IIb con calibración cada 12 meses, registrado por la API.</summary>
    private static async Task<Guid> Equipo(Institucion i, string placa)
    {
        var registro = await i.Ingeniero.PostAsJsonAsync(Equipos, new Dictionary<string, object?>
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
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return (await Cuerpo(registro)).GetProperty("id").GetGuid();
    }

    private async Task<Institucion> NuevaInstitucion()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, _) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.I, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        return new Institucion(tenant, ingeniero, tecnico, tecnicoId, tipo, ubicacion);
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
