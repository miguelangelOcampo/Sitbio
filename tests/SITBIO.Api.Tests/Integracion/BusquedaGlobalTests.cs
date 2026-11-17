using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Búsqueda global (D-04) contra la API y PostgreSQL reales: equipos por placa o serie, tiquetes por código y
/// órdenes por referencia, máximo cinco por grupo, solo de la institución del token y con el alcance de cada rol.
/// </summary>
public class BusquedaGlobalTests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Busqueda = "/api/v1/busqueda";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private sealed record Institucion(Guid Tenant, HttpClient Ingeniero, HttpClient Tecnico, Guid TecnicoId, HttpClient Clinico, Guid Tipo, Guid Ubicacion);

    [FactIntegracion]
    public async Task Ingeniero_EncuentraEquiposTiquetesYOrdenes_SoloDeSuInstitucion()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "BUSQ-A1");
        var tiquete = await Reportar(i.Clinico, equipo);
        var orden = await Asignar(i, tiquete);

        // La otra institución tiene la misma placa y su propio TQT-000001: no aparecen.
        var otra = await NuevaInstitucion();
        await Reportar(otra.Clinico, await Equipo(otra, "BUSQ-A1"));

        var porPlaca = await Buscar(i.Ingeniero, "busq-a1");
        var equipos = porPlaca.GetProperty("equipos");
        Assert.Equal(1, equipos.GetArrayLength());
        Assert.Equal((equipo, "BUSQ-A1", "Monitor de signos vitales", "UCI adultos"), (
            equipos[0].GetProperty("id").GetGuid(),
            equipos[0].GetProperty("placaInventario").GetString(),
            equipos[0].GetProperty("tipoEquipo").GetString(),
            equipos[0].GetProperty("ubicacion").GetString()));

        var porSerie = await Buscar(i.Ingeniero, "SN-BUSQ");
        Assert.Equal(equipo, porSerie.GetProperty("equipos")[0].GetProperty("id").GetGuid());

        var porCodigo = await Buscar(i.Ingeniero, tiquete);
        var tiquetes = porCodigo.GetProperty("tiquetes");
        Assert.Equal(1, tiquetes.GetArrayLength());
        Assert.Equal((tiquete, "BUSQ-A1"), (tiquetes[0].GetProperty("codigo").GetString(), tiquetes[0].GetProperty("placaInventario").GetString()));
        var ordenes = porCodigo.GetProperty("ordenes");
        Assert.Equal(1, ordenes.GetArrayLength());
        Assert.Equal((orden, tiquete), (ordenes[0].GetProperty("id").GetGuid(), ordenes[0].GetProperty("referencia").GetString()));
    }

    [FactIntegracion]
    public async Task MaximoCincoPorGrupo_YMenosDeDosCaracteres_NoBusca()
    {
        var i = await NuevaInstitucion();
        await api.LlenarEquiposAsync(i.Tenant, i.Tipo, i.Ubicacion, 7, "LOTE");

        Assert.Equal(5, (await Buscar(i.Ingeniero, "LOTE-P")).GetProperty("equipos").GetArrayLength());
        Assert.Equal(0, (await Buscar(i.Ingeniero, "L")).GetProperty("equipos").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await i.Ingeniero.GetAsync($"{Busqueda}?q={new string('x', 61)}")).StatusCode);
    }

    [FactIntegracion]
    public async Task PersonalClinico_SoloVeSusTiquetes_YNingunaOrden()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "BUSQ-C1");
        var propio = await Reportar(i.Clinico, equipo);
        var (otroClinico, _) = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        var ajeno = await Reportar(otroClinico, equipo);
        await Asignar(i, propio);

        var resultado = await Buscar(i.Clinico, "TQT-");

        Assert.Equal([propio], resultado.GetProperty("tiquetes").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));
        Assert.DoesNotContain(ajeno, resultado.GetProperty("tiquetes").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));
        Assert.Equal(0, resultado.GetProperty("ordenes").GetArrayLength());
        Assert.Equal(1, (await Buscar(i.Ingeniero, "TQT-")).GetProperty("ordenes").GetArrayLength());
    }

    [FactIntegracion]
    public async Task Tecnico_SoloVeLasOrdenesAsignadasAEl()
    {
        var i = await NuevaInstitucion();
        var equipo = await Equipo(i, "BUSQ-T1");
        var suya = await Asignar(i, await Reportar(i.Clinico, equipo));
        var (_, otroTecnicoId) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        await Asignar(i with { TecnicoId = otroTecnicoId }, await Reportar(i.Clinico, equipo));

        var ordenes = (await Buscar(i.Tecnico, "TQT-")).GetProperty("ordenes");

        Assert.Equal([suya], ordenes.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
    }

    [FactIntegracion]
    public async Task SinToken401_YDirectiva403()
    {
        var i = await NuevaInstitucion();
        var (directiva, _) = await SesionDe(i.Tenant, CodigosRol.DirectivaInstitucional);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"{Busqueda}?q=MON")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await directiva.GetAsync($"{Busqueda}?q=MON")).StatusCode);
    }

    // ---------- Apoyo ----------

    private static async Task<JsonElement> Buscar(HttpClient cliente, string q)
    {
        var respuesta = await cliente.GetAsync($"{Busqueda}?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    private static async Task<string> Reportar(HttpClient cliente, Guid equipo)
    {
        var reporte = await cliente.PostAsJsonAsync("/api/v1/tiquetes", new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende.", criticidad = Criticidades.Media });
        Assert.Equal(HttpStatusCode.Created, reporte.StatusCode);
        return (await Cuerpo(reporte)).GetProperty("codigo").GetString()!;
    }

    private static async Task<Guid> Asignar(Institucion i, string codigoTiquete)
    {
        var tiquete = await Cuerpo(await i.Ingeniero.GetAsync($"/api/v1/tiquetes/{codigoTiquete}"));
        var asignada = await i.Ingeniero.PostAsJsonAsync("/api/v1/ordenes", new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId });
        Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
        return (await Cuerpo(asignada)).GetProperty("id").GetGuid();
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
