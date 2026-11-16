using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-017 (DOC-04; CP-021) contra la API y PostgreSQL reales: los usuarios
/// inician sesión de verdad, reportan con API-TIQ y siguen el tiquete por su código. Los cambios de
/// estado que todavía no tienen pantalla (asignación, ejecución y cierre) se escriben en la base con su
/// historial. Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu017Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/tiquetes";
    private const string FallaNoEnciende = "No enciende";
    private const string NombrePrueba = "Prueba HU-001";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, Guid IngenieroId, HttpClient Clinico, Guid ClinicoId, Guid Equipo);

    // ---------- CA-HU-017-01 (CP-021) ----------

    [FactIntegracion]
    public async Task CA_HU_017_01_ConElCodigo_VeElEstadoYElHistorialDeCambios()
    {
        var i = await InstitucionConEquipo();
        var codigo = (await Reportar(i.Clinico, i.Equipo, Criticidades.Alta)).GetProperty("codigo").GetString()!;

        // Recién reportado: «recibido» y un solo cambio, sin estado anterior, a nombre de quien reportó.
        var recibido = await Cuerpo(await i.Clinico.GetAsync($"{Ruta}/{codigo.ToLowerInvariant()}"));
        Assert.Equal((codigo, EstadosTiquete.Recibido), (recibido.GetProperty("codigo").GetString(), recibido.GetProperty("estado").GetString()));
        var inicial = Assert.Single((await Historial(i.Clinico, codigo)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, inicial.GetProperty("estadoAnterior").ValueKind);
        Assert.Equal((EstadosTiquete.Recibido, i.ClinicoId), (inicial.GetProperty("nuevoEstado").GetString(), inicial.GetProperty("usuarioId").GetGuid()));

        // Recorre los cuatro estados y en cada paso el Personal Clínico ve el estado actual.
        var cambios = new[]
        {
            (EstadosTiquete.Asignado, "Asignado al técnico de turno"),
            (EstadosTiquete.EnEjecucion, "El técnico inició la intervención"),
            (EstadosTiquete.Cerrado, "Equipo reparado y probado"),
        };
        var fecha = DateTimeOffset.UtcNow;
        foreach (var (estado, comentario) in cambios)
        {
            fecha = fecha.AddMinutes(10);
            await api.CambiarEstadoTiqueteAsync(i.Tenant, codigo, i.IngenieroId, estado, comentario, fecha);
            Assert.Equal(estado, (await Cuerpo(await i.Clinico.GetAsync($"{Ruta}/{codigo}"))).GetProperty("estado").GetString());
        }

        var cerrado = await Cuerpo(await i.Clinico.GetAsync($"{Ruta}/{codigo}"));
        Assert.Equal(fecha, cerrado.GetProperty("fechaCierre").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));

        // El historial, del más antiguo al más reciente, con el estado anterior, el comentario y quién hizo cada cambio.
        var historial = (await Historial(i.Clinico, codigo)).EnumerateArray().ToList();
        Assert.Equal(
            [
                ((string?)null, EstadosTiquete.Recibido, "Falla reportada."),
                (EstadosTiquete.Recibido, EstadosTiquete.Asignado, "Asignado al técnico de turno"),
                (EstadosTiquete.Asignado, EstadosTiquete.EnEjecucion, "El técnico inició la intervención"),
                (EstadosTiquete.EnEjecucion, EstadosTiquete.Cerrado, "Equipo reparado y probado"),
            ],
            historial.Select(h => (h.GetProperty("estadoAnterior").GetString(), h.GetProperty("nuevoEstado").GetString(), h.GetProperty("comentario").GetString())));
        Assert.Equal([i.ClinicoId, i.IngenieroId, i.IngenieroId, i.IngenieroId], historial.Select(h => h.GetProperty("usuarioId").GetGuid()));
        Assert.All(historial, h => Assert.Equal(NombrePrueba, h.GetProperty("usuario").GetString()));
        var fechas = historial.Select(h => h.GetProperty("fechaCambio").GetDateTimeOffset()).ToList();
        Assert.Equal(fechas.Order(), fechas);

        // Un código que no existe responde 404, también para el historial.
        Assert.Equal(HttpStatusCode.NotFound, (await i.Clinico.GetAsync($"{Ruta}/TQT-999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await i.Clinico.GetAsync($"{Ruta}/TQT-999999/historial")).StatusCode);
    }

    // ---------- CA-HU-017-02 (CP-021) ----------

    [FactIntegracion]
    public async Task CA_HU_017_02_ElPersonalClinico_SoloVeLosTiquetesQueReporto()
    {
        var i = await InstitucionConEquipo();
        var (otroClinico, _) = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        var propio = await Codigo(i.Clinico, i.Equipo, Criticidades.Media);
        var deOtroClinico = await Codigo(otroClinico, i.Equipo, Criticidades.Baja);
        var delIngeniero = await Codigo(i.Ingeniero, i.Equipo, Criticidades.Alta);

        // Sin pedirlo, la lista del Personal Clínico trae solo lo suyo; con «mios» es lo mismo.
        Assert.Equal([propio], await Codigos(i.Clinico, Ruta));
        Assert.Equal([propio], await Codigos(i.Clinico, $"{Ruta}?mios=true"));
        Assert.Equal([deOtroClinico], await Codigos(otroClinico, Ruta));

        // Los tiquetes de otras personas responden como si no existieran, también su historial.
        foreach (var ajeno in new[] { deOtroClinico, delIngeniero })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await i.Clinico.GetAsync($"{Ruta}/{ajeno}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await i.Clinico.GetAsync($"{Ruta}/{ajeno}/historial")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await i.Clinico.GetAsync($"{Ruta}/{propio}/historial")).StatusCode);

        // El Ingeniero Biomédico ve todos y, con «mios», solo los que él reportó.
        Assert.Equal([delIngeniero, deOtroClinico, propio], await Codigos(i.Ingeniero, Ruta));
        Assert.Equal([delIngeniero], await Codigos(i.Ingeniero, $"{Ruta}?mios=true"));
        Assert.Equal(HttpStatusCode.OK, (await i.Ingeniero.GetAsync($"{Ruta}/{propio}/historial")).StatusCode);
    }

    // ---------- Permisos (matriz del DOC-03) y aislamiento (RN-008) ----------

    [FactIntegracion]
    public async Task Permisos_ConsultanAdministradorTecnicoYEnteRegulador_GerenteYDirectivaNo()
    {
        var i = await InstitucionConEquipo();
        var codigo = await Codigo(i.Clinico, i.Equipo, Criticidades.Media);
        await Codigo(i.Ingeniero, i.Equipo, Criticidades.Baja);

        foreach (var rol in new[] { CodigosRol.AdministradorSistema, CodigosRol.TecnicoMantenimiento, CodigosRol.EnteRegulador })
        {
            var (cliente, _) = await SesionDe(i.Tenant, rol);
            Assert.Equal(2, (await Cuerpo(await cliente.GetAsync(Ruta))).GetProperty("total").GetInt32());
            Assert.Equal(EstadosTiquete.Recibido, (await Cuerpo(await cliente.GetAsync($"{Ruta}/{codigo}"))).GetProperty("estado").GetString());
            Assert.Single((await Historial(cliente, codigo)).EnumerateArray());
        }

        // El Ente Regulador consulta, pero no reporta (solo lectura).
        var (regulador, _) = await SesionDe(i.Tenant, CodigosRol.EnteRegulador);
        Assert.Equal(HttpStatusCode.Forbidden, (await regulador.PostAsJsonAsync(Ruta, Falla(i.Equipo, Criticidades.Baja))).StatusCode);

        foreach (var rol in new[] { CodigosRol.GerenteBiomedico, CodigosRol.DirectivaInstitucional })
        {
            var (cliente, _) = await SesionDe(i.Tenant, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/{codigo}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{Ruta}/{codigo}/historial")).StatusCode);
        }

        var anonimo = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"{Ruta}/{codigo}/historial")).StatusCode);
    }

    [FactIntegracion]
    public async Task OtraInstitucion_NoSigueTiquetesAjenos()
    {
        var a = await InstitucionConEquipo();
        var b = await InstitucionConEquipo();
        var codigo = await Codigo(a.Clinico, a.Equipo, Criticidades.Alta);
        await api.CambiarEstadoTiqueteAsync(a.Tenant, codigo, a.IngenieroId, EstadosTiquete.Asignado, "Asignado", DateTimeOffset.UtcNow.AddMinutes(5));

        // b no tiene tiquetes: el mismo código no existe para ella.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{codigo}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{codigo}/historial")).StatusCode);
        Assert.Empty(await Codigos(b.Ingeniero, Ruta));

        // Cuando b reporta, su TQT-000001 es otro tiquete con su propio historial.
        var propio = await Codigo(b.Clinico, b.Equipo, Criticidades.Baja);
        Assert.Equal(codigo, propio);
        Assert.Single((await Historial(b.Clinico, propio)).EnumerateArray());
        Assert.Equal(2, (await Historial(a.Clinico, codigo)).GetArrayLength());
    }

    private async Task<Institucion> InstitucionConEquipo()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, ingenieroId) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (clinico, clinicoId) = await SesionDe(tenant, CodigosRol.PersonalClinico);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);

        var tipo = await api.CrearTipoEquipoAsync(tenant, "Bomba de infusión", ClasesRiesgo.IIb, true, 12);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Hospitalización");
        var registro = await ingeniero.PostAsJsonAsync("/api/v1/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = "SN-BOM-01",
            ["placaInventario"] = "BOM-01",
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
        return new Institucion(tenant, ingeniero, ingenieroId, clinico, clinicoId, (await Cuerpo(registro)).GetProperty("id").GetGuid());
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static object Falla(Guid equipo, string criticidad) =>
        new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "Alarma de oclusión sin causa.", criticidad };

    private static async Task<JsonElement> Reportar(HttpClient cliente, Guid equipo, string criticidad)
    {
        var respuesta = await cliente.PostAsJsonAsync(Ruta, Falla(equipo, criticidad));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    private static async Task<string> Codigo(HttpClient cliente, Guid equipo, string criticidad) =>
        (await Reportar(cliente, equipo, criticidad)).GetProperty("codigo").GetString()!;

    private static async Task<JsonElement> Historial(HttpClient cliente, string codigo)
    {
        var respuesta = await cliente.GetAsync($"{Ruta}/{codigo}/historial");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    private static async Task<List<string?>> Codigos(HttpClient cliente, string ruta) =>
        (await Cuerpo(await cliente.GetAsync(ruta))).GetProperty("items").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()).ToList();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
