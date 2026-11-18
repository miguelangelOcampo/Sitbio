using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using MongoDB.Bson;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-016 (DOC-04; CP-020) contra la API, PostgreSQL y MongoDB reales: con
/// la plantilla en CSV o Excel se registran los equipos válidos en la institución del token
/// (CA-HU-016-01); las filas con serie o placa repetida o con datos inválidos se omiten con un informe
/// por fila (CA-HU-016-02, RN-001, RN-010); no se pasa del máximo de equipos activos del plan
/// (CA-HU-016-03, RN-009). Solo AC-01 y AC-02 importan; otra institución no se ve afectada (RN-008).
/// </summary>
public class CriteriosHu016Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/equipos/importacion";
    private static readonly string Vence = FechaColombia.Hoy(TimeProvider.System).AddYears(2).ToString("yyyy-MM-dd");
    private static readonly string Encabezado = string.Join(';', ColumnasImportacion.Todas);

    private sealed record Institucion(Guid Tenant, Guid Tipo, Guid Ubicacion, HttpClient Ingeniero, string Prefijo);

    private async Task<Institucion> NuevaInstitucion(int maxEquipos = 1000)
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, maxEquipos, 25);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI adultos");
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.IIb, true, 12);
        var correo = api.Correo($"h16-ingeniero-{tenant:N}");
        await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        return new Institucion(tenant, tipo, ubicacion, await api.IniciarSesionAsync(correo), $"I{tenant.ToString("N")[..6]}");
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"h16-{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    /// <summary>Fila válida de la plantilla; con <paramref name="cambios"/> se reemplazan columnas.</summary>
    private static string Fila(string placa, string serie, params (string Columna, string Valor)[] cambios)
    {
        var valores = new Dictionary<string, string>
        {
            [ColumnasImportacion.TipoEquipo] = "monitor de signos VITALES",
            [ColumnasImportacion.Ubicacion] = "UCI Adultos",
            [ColumnasImportacion.NumeroSerie] = serie,
            [ColumnasImportacion.Placa] = placa,
            [ColumnasImportacion.Marca] = "Mindray",
            [ColumnasImportacion.Modelo] = "uMEC 12",
            [ColumnasImportacion.Proveedor] = string.Empty,
            [ColumnasImportacion.FechaAdquisicion] = "15/03/2024",
            [ColumnasImportacion.TipoAutorizacion] = "Registro sanitario",
            [ColumnasImportacion.RegistroInvima] = "INVIMA 2020DM-0021345",
            [ColumnasImportacion.VenceRegistro] = Vence,
            [ColumnasImportacion.ClaseRiesgo] = "iib",
            [ColumnasImportacion.RequiereCalibracion] = "Sí",
            [ColumnasImportacion.Periodicidad] = "12",
        };
        foreach (var (columna, valor) in cambios)
        {
            valores[columna] = valor;
        }

        return string.Join(';', ColumnasImportacion.Todas.Select(c => valores[c]));
    }

    private static byte[] Csv(params string[] filas) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", [Encabezado, .. filas]) + "\r\n")];

    private static Task<HttpResponseMessage> Importar(HttpClient cliente, byte[] archivo, bool confirmar, string nombre = "inventario.csv")
    {
        var formulario = new MultipartFormDataContent();
        formulario.Add(new StringContent(confirmar ? "true" : "false"), "confirmar");
        var contenido = new ByteArrayContent(archivo);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formulario.Add(contenido, "archivo", nombre);
        return cliente.PostAsync(Ruta, formulario);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static List<(int Fila, string Columna, string Mensaje)> Errores(JsonElement resultado) =>
        resultado.GetProperty("errores").EnumerateArray()
            .Select(e => (e.GetProperty("fila").GetInt32(), e.GetProperty("columna").GetString()!, e.GetProperty("mensaje").GetString()!))
            .ToList();

    // ---------- CA-HU-016-01 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_016_01_ArchivoValido_LaVistaPreviaNoGuardaYAlConfirmarSeRegistranEnLaInstitucion()
    {
        var i = await NuevaInstitucion();
        var archivo = Csv(Fila($"{i.Prefijo}-01", $"SN-{i.Prefijo}-01"), Fila($"{i.Prefijo}-02", $"SN-{i.Prefijo}-02"));

        var vista = await Importar(i.Ingeniero, archivo, confirmar: false);

        Assert.Equal(HttpStatusCode.OK, vista.StatusCode);
        var previa = await Cuerpo(vista);
        Assert.Equal((false, 2, 2, 2, 0), (previa.GetProperty("confirmada").GetBoolean(), previa.GetProperty("totalFilas").GetInt32(),
            previa.GetProperty("filasValidas").GetInt32(), previa.GetProperty("importables").GetInt32(), previa.GetProperty("importadas").GetInt32()));
        Assert.Empty(Errores(previa));
        Assert.Equal(0, await api.ContarEquiposAsync(i.Tenant));

        var confirmada = await Cuerpo(await Importar(i.Ingeniero, archivo, confirmar: true));

        Assert.Equal(2, confirmada.GetProperty("importadas").GetInt32());
        Assert.Equal(2, await api.ContarEquiposAsync(i.Tenant));
        var lista = await Cuerpo(await i.Ingeniero.GetAsync($"/api/v1/equipos?placa={i.Prefijo}"));
        var equipo = lista.GetProperty("items").EnumerateArray().First(e => e.GetProperty("placaInventario").GetString() == $"{i.Prefijo}-01");
        Assert.Equal("Monitor de signos vitales", equipo.GetProperty("tipoEquipo").GetString());
        Assert.Equal("UCI adultos", equipo.GetProperty("ubicacion").GetString());
        Assert.Equal("OPERATIVO", equipo.GetProperty("estado").GetString());
        Assert.Equal(ClasesRiesgo.IIb, equipo.GetProperty("claseRiesgo").GetString());

        // Bitácora IMPORTAR_CSV con los conteos, sin placas ni series.
        var evento = (await api.LeerBitacoraAsync(i.Tenant)).Single(d => d["accion"] == AccionesAuditoriaImportar);
        Assert.Equal("2", evento["detalle"]["importadas"].AsString);
        Assert.Equal("csv", evento["detalle"]["formato"].AsString);
        Assert.DoesNotContain(i.Prefijo, evento.ToJson());
    }

    private const string AccionesAuditoriaImportar = "IMPORTAR_CSV";

    [FactIntegracion]
    public async Task CA_HU_016_01_LaPlantillaDeExcel_SeLlenaYSeImporta()
    {
        var i = await NuevaInstitucion();
        var descarga = await i.Ingeniero.GetAsync($"{Ruta}/plantilla?formato=xlsx");
        Assert.Equal(HttpStatusCode.OK, descarga.StatusCode);

        using var libro = new XLWorkbook(new MemoryStream(await descarga.Content.ReadAsByteArrayAsync()));
        var hoja = libro.Worksheet("Equipos");
        Assert.Equal(ColumnasImportacion.Todas, Enumerable.Range(1, ColumnasImportacion.Todas.Count).Select(c => hoja.Cell(1, c).GetString()));
        Assert.Contains("Monitor de signos vitales", libro.Worksheet("Valores permitidos").Column(1).CellsUsed().Select(c => c.GetString()));

        var valores = Fila($"{i.Prefijo}-X1", $"0000{i.Prefijo}").Split(';');
        for (var c = 0; c < valores.Length; c++)
        {
            hoja.Cell(2, c + 1).SetValue(valores[c]);
        }

        hoja.Cell(2, ColumnasImportacion.Todas.ToList().IndexOf(ColumnasImportacion.VenceRegistro) + 1).SetValue(DateTime.Today.AddYears(3));
        using var lleno = new MemoryStream();
        libro.SaveAs(lleno);

        var resultado = await Cuerpo(await Importar(i.Ingeniero, lleno.ToArray(), confirmar: true, nombre: "plantilla.xlsx"));

        Assert.Empty(Errores(resultado));
        Assert.Equal(1, resultado.GetProperty("importadas").GetInt32());
        var csv = await i.Ingeniero.GetAsync($"{Ruta}/plantilla?formato=csv");
        Assert.Equal(Encabezado, (await csv.Content.ReadAsStringAsync()).TrimStart('﻿').TrimEnd());
    }

    // ---------- CA-HU-016-02 ----------

    [FactIntegracion]
    public async Task CA_HU_016_02_DuplicadosYFilasInvalidas_SeOmitenConInformePorFila_YLasValidasSeImportan()
    {
        var i = await NuevaInstitucion();
        await api.LlenarEquiposAsync(i.Tenant, i.Tipo, i.Ubicacion, 1, i.Prefijo);
        var existente = (await Cuerpo(await i.Ingeniero.GetAsync("/api/v1/equipos"))).GetProperty("items")[0];
        var placaExistente = existente.GetProperty("placaInventario").GetString()!;

        var archivo = Csv(
            Fila($"{i.Prefijo}-A", $"SN-{i.Prefijo}-A"),                                   // 2: válida
            Fila(placaExistente, $"SN-{i.Prefijo}-B"),                                     // 3: placa ya registrada (RN-001)
            Fila($"{i.Prefijo}-A", $"SN-{i.Prefijo}-C"),                                   // 4: placa repetida en el archivo
            Fila($"{i.Prefijo}-D", $"SN-{i.Prefijo}-D", (ColumnasImportacion.TipoEquipo, "Resonador")), // 5: tipo inexistente
            Fila($"{i.Prefijo}-E", $"SN-{i.Prefijo}-E", (ColumnasImportacion.VenceRegistro, ""), (ColumnasImportacion.ClaseRiesgo, "IV")), // 6: RN-010
            Fila($"{i.Prefijo}-F", $"SN-{i.Prefijo}-F"));                                  // 7: válida

        var resultado = await Cuerpo(await Importar(i.Ingeniero, archivo, confirmar: true));

        Assert.Equal((6, 2, 2), (resultado.GetProperty("totalFilas").GetInt32(), resultado.GetProperty("filasValidas").GetInt32(), resultado.GetProperty("importadas").GetInt32()));
        var errores = Errores(resultado);
        Assert.Contains(errores, e => e.Fila == 3 && e.Columna == ColumnasImportacion.Placa && e.Mensaje.Contains("Ya existe", StringComparison.Ordinal));
        Assert.Contains(errores, e => e.Fila == 4 && e.Columna == ColumnasImportacion.Placa && e.Mensaje.Contains("se repite en el archivo", StringComparison.Ordinal));
        Assert.Contains(errores, e => e.Fila == 5 && e.Columna == ColumnasImportacion.TipoEquipo && e.Mensaje.Contains("«Resonador» no existe", StringComparison.Ordinal));
        Assert.Contains(errores, e => e.Fila == 6 && e.Columna == ColumnasImportacion.VenceRegistro);
        Assert.Contains(errores, e => e.Fila == 6 && e.Columna == ColumnasImportacion.ClaseRiesgo);
        Assert.DoesNotContain(errores, e => e.Fila is 2 or 7);
        Assert.Equal(3, await api.ContarEquiposAsync(i.Tenant));
    }

    [FactIntegracion]
    public async Task CA_HU_016_02_ArchivoSinLasColumnasOConOtroFormato_SeRechazaCon400()
    {
        var i = await NuevaInstitucion();

        var sinColumnas = await Importar(i.Ingeniero, Encoding.UTF8.GetBytes("Placa;Marca\r\nP-1;Mindray\r\n"), confirmar: false);
        var otroFormato = await Importar(i.Ingeniero, Encoding.UTF8.GetBytes("hola"), confirmar: false, nombre: "inventario.txt");
        var excelDanado = await Importar(i.Ingeniero, Encoding.UTF8.GetBytes("no es un libro"), confirmar: false, nombre: "inventario.xlsx");

        Assert.All(new[] { sinColumnas, otroFormato, excelDanado }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("Faltan columnas de la plantilla", (await Cuerpo(sinColumnas)).GetProperty("errors").GetProperty("archivo")[0].GetString());
        Assert.Equal(ImportacionEquipos.FormatoNoPermitido, (await Cuerpo(otroFormato)).GetProperty("errors").GetProperty("archivo")[0].GetString());
        Assert.Equal(0, await api.ContarEquiposAsync(i.Tenant));
    }

    // ---------- CA-HU-016-03 ----------

    [FactIntegracion]
    public async Task CA_HU_016_03_MasFilasValidasQueElCupo_InformaCuantasCabenYNoSuperaElPlan()
    {
        var i = await NuevaInstitucion(maxEquipos: 3);
        await api.LlenarEquiposAsync(i.Tenant, i.Tipo, i.Ubicacion, 1, i.Prefijo);
        var archivo = Csv(Enumerable.Range(1, 4).Select(n => Fila($"{i.Prefijo}-Q{n}", $"SN-{i.Prefijo}-Q{n}")).ToArray());

        var previa = await Cuerpo(await Importar(i.Ingeniero, archivo, confirmar: false));

        Assert.Equal((4, 2, 2), (previa.GetProperty("filasValidas").GetInt32(), previa.GetProperty("cupoDisponible").GetInt32(), previa.GetProperty("importables").GetInt32()));
        Assert.Equal([4, 5], Errores(previa).Where(e => e.Columna == ImportacionEquipos.ColumnaPlan).Select(e => e.Fila));

        var confirmada = await Cuerpo(await Importar(i.Ingeniero, archivo, confirmar: true));

        Assert.Equal(2, confirmada.GetProperty("importadas").GetInt32());
        Assert.Equal(3, await api.ContarEquiposAsync(i.Tenant));

        // Con el plan lleno no se importa nada.
        var lleno = await Cuerpo(await Importar(i.Ingeniero, Csv(Fila($"{i.Prefijo}-Z", $"SN-{i.Prefijo}-Z")), confirmar: true));
        Assert.Equal((0, 0), (lleno.GetProperty("cupoDisponible").GetInt32(), lleno.GetProperty("importadas").GetInt32()));
        Assert.Equal(3, await api.ContarEquiposAsync(i.Tenant));
    }

    // ---------- RBAC (matriz de permisos) ----------

    [FactIntegracion]
    public async Task SoloElAdministradorYElIngenieroImportan()
    {
        var i = await NuevaInstitucion();
        var administrador = await SesionDe(i.Tenant, CodigosRol.AdministradorSistema);

        var porAdministrador = await Importar(administrador, Csv(Fila($"{i.Prefijo}-AD", $"SN-{i.Prefijo}-AD")), confirmar: true);
        Assert.Equal(HttpStatusCode.OK, porAdministrador.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrador.GetAsync($"{Ruta}/plantilla")).StatusCode);

        foreach (var rol in new[] { CodigosRol.TecnicoMantenimiento, CodigosRol.PersonalClinico, CodigosRol.GerenteBiomedico, CodigosRol.DirectivaInstitucional, CodigosRol.EnteRegulador })
        {
            var cliente = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await Importar(cliente, Csv(Fila($"{i.Prefijo}-{rol}", $"SN-{i.Prefijo}-{rol}")), confirmar: true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/plantilla")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Ruta}/plantilla")).StatusCode);
        Assert.Equal(1, await api.ContarEquiposAsync(i.Tenant));
    }

    // ---------- Aislamiento entre tenants (RN-008, SEC-003) ----------

    [FactIntegracion]
    public async Task OtraInstitucion_NoUsaLosCatalogosNiRecibeLosEquipos()
    {
        var a = await NuevaInstitucion();
        var b = await NuevaInstitucion();
        await api.CrearUbicacionAsync(a.Tenant, "Quirófano 3");

        // La ubicación solo existe en A: para B la fila es inválida.
        var deB = await Cuerpo(await Importar(b.Ingeniero, Csv(Fila($"{b.Prefijo}-1", $"SN-{b.Prefijo}-1", (ColumnasImportacion.Ubicacion, "Quirófano 3"))), confirmar: true));
        Assert.Contains(Errores(deB), e => e.Columna == ColumnasImportacion.Ubicacion);

        // La misma placa de A se puede usar en B (RN-001 es por institución) y queda en B.
        await Importar(a.Ingeniero, Csv(Fila("COMPARTIDA-1", "SN-COMPARTIDA-1")), confirmar: true);
        var compartida = await Cuerpo(await Importar(b.Ingeniero, Csv(Fila("COMPARTIDA-1", "SN-COMPARTIDA-1")), confirmar: true));

        Assert.Equal(1, compartida.GetProperty("importadas").GetInt32());
        Assert.Equal((1, 1), (await api.ContarEquiposAsync(a.Tenant), await api.ContarEquiposAsync(b.Tenant)));
        var plantillaB = await (await b.Ingeniero.GetAsync($"{Ruta}/plantilla?formato=xlsx")).Content.ReadAsByteArrayAsync();
        using var libro = new XLWorkbook(new MemoryStream(plantillaB));
        Assert.DoesNotContain("Quirófano 3", libro.Worksheet("Valores permitidos").Column(2).CellsUsed().Select(c => c.GetString()));
    }
}
