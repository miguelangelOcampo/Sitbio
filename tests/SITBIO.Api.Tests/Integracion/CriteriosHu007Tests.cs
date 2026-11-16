using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Sitbio.Application.Ordenes;
using Sitbio.Application.Reportes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-007 (CP-013) y HU-027 (CP-031) contra la API, PostgreSQL y MongoDB reales: el
/// historial de órdenes de un rango sale en PDF (CA-HU-007-01) y en Excel con las mismas columnas
/// (CA-HU-007-02); la hoja de vida del equipo sale en PDF con sus datos e intervenciones (CA-HU-027-01). El
/// tiempo con 1 000 registros (RNF-007, CA-HU-007-01 y CA-HU-027-02) lo mide <c>GeneradorReportesTests</c>.
/// Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu007Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Equipos = "/api/v1/equipos";
    private const string Ordenes = "/api/v1/ordenes";
    private const string Reporte = "/api/v1/reportes/ordenes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, HttpClient Tecnico, Guid TecnicoId, HttpClient Clinico, Guid Tipo, Guid Ubicacion);

    // ---------- CA-HU-007-01: historial del rango en PDF ----------

    [FactIntegracion]
    public async Task CA_HU_007_01_HistorialDelRango_SaleEnPdf()
    {
        var i = await NuevaInstitucion();
        await OrdenAsignada(i, await Equipo(i, "MON-71"));
        var (gerente, _) = await SesionDe(i.Tenant, CodigosRol.GerenteBiomedico);

        var respuesta = await gerente.GetAsync(Ruta(Hoy.AddDays(-7), Hoy, "pdf"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(FormatosReporte.TipoPdf, respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"historial-ordenes-{Compacta(Hoy.AddDays(-7))}-{Compacta(Hoy)}.pdf", respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(await respuesta.Content.ReadAsByteArrayAsync(), 0, 4));
    }

    [FactIntegracion]
    public async Task CA_HU_007_01_RangoOFormatoInvalidos_Responden400_YElTecnicoNoExporta()
    {
        var i = await NuevaInstitucion();

        var invertido = await i.Ingeniero.GetAsync(Ruta(Hoy, Hoy.AddDays(-1), "pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, invertido.StatusCode);
        Assert.True((await Cuerpo(invertido)).GetProperty("errors").TryGetProperty("hasta", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await i.Ingeniero.GetAsync(Ruta(Hoy, Hoy, "docx"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync(Ruta(Hoy, Hoy, "pdf"))).StatusCode);
    }

    // ---------- CA-HU-007-02: Excel con las mismas columnas, solo el rango y la institución ----------

    [FactIntegracion]
    public async Task CA_HU_007_02_Excel_TieneLasMismasColumnas_YSoloLasOrdenesDelRangoYLaInstitucion()
    {
        var i = await NuevaInstitucion();
        await OrdenAsignada(i, await Equipo(i, "MON-72"));
        await OrdenAsignada(i, await Equipo(i, "MON-73"));
        var otra = await NuevaInstitucion();
        await OrdenAsignada(otra, await Equipo(otra, "OTR-01"));

        var hoja = await Excel(i.Ingeniero, Hoy.AddDays(-30), Hoy);

        Assert.Equal(ColumnasHistorial.Nombres, Enumerable.Range(1, ColumnasHistorial.Nombres.Count).Select(c => hoja.Cell(5, c).GetString()));
        var placas = Enumerable.Range(6, hoja.LastRowUsed()!.RowNumber() - 5).Select(f => hoja.Cell(f, 2).GetString()).ToList();
        Assert.Equal(["MON-72", "MON-73"], placas);
        Assert.Equal(("Correctivo", "Asignada"), (hoja.Cell(6, 5).GetString(), hoja.Cell(6, 8).GetString()));
        Assert.StartsWith(Tiquete.Prefijo, hoja.Cell(6, 6).GetString());

        // Un rango sin órdenes da el archivo con los títulos y sin filas.
        var vacia = await Excel(i.Ingeniero, Hoy.AddDays(-60), Hoy.AddDays(-31));
        Assert.Equal(5, vacia.LastRowUsed()!.RowNumber());
    }

    // ---------- CA-HU-027-01: hoja de vida en PDF ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_027_01_HojaDeVida_SaleEnPdf_ParaElAuditor_YNoParaOtraInstitucion()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "MON-74");
        var orden = await OrdenAsignada(i, equipo);
        await Atender(i, orden);
        var (auditor, _) = await SesionDe(i.Tenant, CodigosRol.EnteRegulador);

        var respuesta = await auditor.GetAsync($"{Equipos}/{equipo}/hoja-vida.pdf");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(("application/pdf", "hoja-de-vida-MON-74.pdf"), (respuesta.Content.Headers.ContentType!.MediaType, respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"')));
        var pdf = await respuesta.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        // La misma historia que el PDF incluye: datos del equipo e intervención registrada.
        var linea = await Cuerpo(await auditor.GetAsync($"{Equipos}/{equipo}/linea-tiempo"));
        Assert.Contains(linea.GetProperty("eventos").EnumerateArray(), e => e.GetProperty("tipo").GetString() == "intervencion");

        var otra = await NuevaInstitucion();
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Ingeniero.GetAsync($"{Equipos}/{equipo}/hoja-vida.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync($"{Equipos}/{equipo}/hoja-vida.pdf")).StatusCode);
    }

    // ---------- Apoyo ----------

    private static string Ruta(DateOnly desde, DateOnly hasta, string formato) =>
        $"{Reporte}?desde={desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&hasta={hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&formato={formato}";

    private static string Compacta(DateOnly fecha) => fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static async Task<IXLWorksheet> Excel(HttpClient cliente, DateOnly desde, DateOnly hasta)
    {
        var respuesta = await cliente.GetAsync(Ruta(desde, hasta, "xlsx"));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(FormatosReporte.TipoExcel, respuesta.Content.Headers.ContentType!.MediaType);
        var libro = new XLWorkbook(new MemoryStream(await respuesta.Content.ReadAsByteArrayAsync()));
        return libro.Worksheet("Historial");
    }

    /// <summary>Falla reportada por el Personal Clínico y orden correctiva asignada al Técnico.</summary>
    private async Task<Guid> OrdenAsignada(Institucion i, Guid equipo)
    {
        var reporte = await i.Clinico.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarlo.", criticidad = Criticidades.Alta });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        var asignada = await i.Ingeniero.PostAsJsonAsync(Ordenes, new { tiqueteId = (await Cuerpo(reporte)).GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        return (await Cuerpo(asignada)).GetProperty("id").GetGuid();
    }

    /// <summary>El Técnico inicia la orden, registra la intervención (MongoDB) y la cierra.</summary>
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
