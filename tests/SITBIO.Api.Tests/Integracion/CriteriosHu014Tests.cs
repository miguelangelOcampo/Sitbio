using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-014 (DOC-04) contra la API, PostgreSQL y MongoDB reales: el
/// Ingeniero Biomédico (AC-02) cambia el estado con motivo, da de baja y revierte la baja; cada
/// cambio se comprueba en <c>historial_estado_equipo</c>. Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu014Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/equipos";
    private static readonly DateOnly Hoy = FechaColombia.Hoy(TimeProvider.System);

    private sealed record Institucion(Guid Tenant, Guid Ingeniero, HttpClient Cliente, Guid Ubicacion, Guid Tipo);

    private async Task<Institucion> InstitucionConIngeniero(string plan = "Clínica", int maxEquipos = 1000)
    {
        var tenant = await api.CrearTenantAsync(plan, 7, maxEquipos, 25);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Urgencias");
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Tensiómetro", ClasesRiesgo.I, true, 12);
        var correo = api.Correo($"h14-ingeniero-{tenant:N}");
        var ingeniero = await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        return new Institucion(tenant, ingeniero, await api.IniciarSesionAsync(correo), ubicacion, tipo);
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"h14-{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static async Task<Guid> Registrar(Institucion i, string placa)
    {
        var respuesta = await i.Cliente.PostAsJsonAsync(Ruta, new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = i.Tipo,
            ["ubicacionId"] = i.Ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = "Welch Allyn",
            ["modelo"] = "DS44",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2019DM-0001234",
            ["venceRegistro"] = Hoy.AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.I,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return Guid.Parse((await Cuerpo(respuesta)).GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> CambiarEstado(HttpClient cliente, Guid equipo, string estado, string motivo) =>
        cliente.PatchAsJsonAsync($"{Ruta}/{equipo}/estado", new { estado, motivo });

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    // ---------- CA-HU-014-01 ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_014_01_CambioConMotivo_QuedaEnElHistorialConFechaYUsuario()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Registrar(i, "H14-0001");
        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);

        var respuesta = await CambiarEstado(i.Cliente, equipo, "FUERA_DE_SERVICIO", "Paciente Ana Ruiz reportó que no enciende.");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("FUERA_DE_SERVICIO", (await Cuerpo(respuesta)).GetProperty("estado").GetString());

        // En PostgreSQL: el alta y el cambio, del más reciente al más antiguo.
        var historial = await api.LeerHistorialAsync(equipo);
        Assert.Equal(2, historial.Count);
        var cambio = historial[0];
        Assert.Equal((EstadosEquipo.Operativo, EstadosEquipo.FueraDeServicio), (cambio.EstadoAnteriorId!.Value, cambio.NuevoEstadoId));
        Assert.Equal("Paciente Ana Ruiz reportó que no enciende.", cambio.Motivo);
        Assert.Equal((i.Tenant, i.Ingeniero), (cambio.TenantId, cambio.UsuarioId));
        Assert.InRange(cambio.FechaCambio, antes, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Null(historial[1].EstadoAnteriorId);

        // La API devuelve lo mismo con los nombres de los estados y del usuario.
        var filas = (await Cuerpo(await i.Cliente.GetAsync($"{Ruta}/{equipo}/estados"))).EnumerateArray().ToList();
        Assert.Equal(["FUERA_DE_SERVICIO", "OPERATIVO"], filas.Select(f => f.GetProperty("estado").GetString()));
        Assert.Equal("Fuera de servicio", filas[0].GetProperty("estadoNombre").GetString());
        Assert.Equal("Prueba HU-001", filas[0].GetProperty("usuario").GetString());

        // En la bitácora queda el cambio, pero no el motivo (texto libre, Ley 1581 de 2012).
        var evento = (await api.LeerBitacoraAsync(i.Tenant))
            .First(d => d["accion"] == "ACTUALIZAR" && d["entidad_id"] == equipo.ToString());
        Assert.Equal("FUERA_DE_SERVICIO", evento["detalle"]["estado_nuevo"].AsString);
        Assert.DoesNotContain("Ana Ruiz", evento.ToJson());
    }

    // ---------- CA-HU-014-02 ----------

    [FactIntegracion]
    public async Task CA_HU_014_02_Baja_DejaDeContarComoActivoYConservaHojaDeVidaEHistorial()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Registrar(i, "H14-0002");
        await Registrar(i, "H14-0003");

        var respuesta = await CambiarEstado(i.Cliente, equipo, "BAJA", "Obsoleto, sin repuestos del fabricante.");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var guardado = await api.LeerEquipoAsync(equipo);
        Assert.Equal(EstadosEquipo.Baja, guardado.EstadoEquipoId);
        Assert.NotNull(guardado.FechaBaja);
        Assert.Equal(("H14-0002", "SN-H14-0002"), (guardado.PlacaInventario, guardado.NumeroSerie));

        // Sigue consultable con su hoja de vida y su historial.
        var hojaDeVida = await Cuerpo(await i.Cliente.GetAsync($"{Ruta}/{equipo}"));
        Assert.Equal("Baja", hojaDeVida.GetProperty("estadoNombre").GetString());
        Assert.Equal(2, (await Cuerpo(await i.Cliente.GetAsync($"{Ruta}/{equipo}/estados"))).GetArrayLength());

        // Ya no figura entre los activos: el filtro por estado lo separa.
        var operativos = await Cuerpo(await i.Cliente.GetAsync($"{Ruta}?estado=OPERATIVO"));
        Assert.Equal(["H14-0003"], operativos.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("placaInventario").GetString()));
        var deBaja = await Cuerpo(await i.Cliente.GetAsync($"{Ruta}?estado=BAJA"));
        Assert.Equal(["H14-0002"], deBaja.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("placaInventario").GetString()));
    }

    [FactIntegracion]
    public async Task Baja_LiberaCupoYLaReversionConElPlanLleno_Responde409Rn009()
    {
        // Consultorio: 300 equipos activos (DOC-01 Tabla 4).
        var i = await InstitucionConIngeniero("Consultorio", 300);
        var equipo = await Registrar(i, "H14-0010");
        Assert.Equal(HttpStatusCode.OK, (await CambiarEstado(i.Cliente, equipo, "BAJA", "Obsoleto")).StatusCode);
        await api.LlenarEquiposAsync(i.Tenant, i.Tipo, i.Ubicacion, 300, "H14L");

        var respuesta = await CambiarEstado(i.Cliente, equipo, "OPERATIVO", "Se consiguieron los repuestos.");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal(GestionEquipos.CodigoLimitePlan, (await Cuerpo(respuesta)).GetProperty("codigo").GetString());
        Assert.Equal(EstadosEquipo.Baja, (await api.LeerEquipoAsync(equipo)).EstadoEquipoId);
        Assert.Equal(2, (await api.LeerHistorialAsync(equipo)).Count);
    }

    [FactIntegracion]
    public async Task Reversion_ConCupo_VuelveAOperativoYQuedaEnElHistorial()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Registrar(i, "H14-0020");
        await CambiarEstado(i.Cliente, equipo, "BAJA", "Dado de baja por error.");

        // Desde la baja solo se puede volver a Operativo.
        var otro = await CambiarEstado(i.Cliente, equipo, "EN_MANTENIMIENTO", "Revisión");
        Assert.Equal(HttpStatusCode.Conflict, otro.StatusCode);
        Assert.Equal(GestionEquipos.CodigoBajaSoloOperativo, (await Cuerpo(otro)).GetProperty("codigo").GetString());

        var respuesta = await CambiarEstado(i.Cliente, equipo, "OPERATIVO", "Se corrige la baja hecha por error.");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var guardado = await api.LeerEquipoAsync(equipo);
        Assert.Equal(EstadosEquipo.Operativo, guardado.EstadoEquipoId);
        Assert.Null(guardado.FechaBaja);
        var historial = await api.LeerHistorialAsync(equipo);
        Assert.Equal([EstadosEquipo.Operativo, EstadosEquipo.Baja, EstadosEquipo.Operativo], historial.Select(h => h.NuevoEstadoId));
    }

    // ---------- Permisos y aislamiento ----------

    [FactIntegracion]
    public async Task SoloElIngenieroCambiaElEstado_LosDemasConsultanElHistorial()
    {
        var i = await InstitucionConIngeniero();
        var equipo = await Registrar(i, "H14-0030");
        var tecnico = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);

        Assert.Equal(HttpStatusCode.Forbidden, (await CambiarEstado(tecnico, equipo, "BAJA", "Obsoleto")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tecnico.GetAsync($"{Ruta}/{equipo}/estados")).StatusCode);
        Assert.Equal(EstadosEquipo.Operativo, (await api.LeerEquipoAsync(equipo)).EstadoEquipoId);
        Assert.Single(await api.LeerHistorialAsync(equipo));
    }

    [FactIntegracion]
    public async Task EquipoDeOtraInstitucion_Responde404YNoCambia()
    {
        var a = await InstitucionConIngeniero();
        var b = await InstitucionConIngeniero();
        var equipo = await Registrar(a, "H14-0040");

        Assert.Equal(HttpStatusCode.NotFound, (await CambiarEstado(b.Cliente, equipo, "BAJA", "Obsoleto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Cliente.GetAsync($"{Ruta}/{equipo}/estados")).StatusCode);
        Assert.Equal(EstadosEquipo.Operativo, (await api.LeerEquipoAsync(equipo)).EstadoEquipoId);
    }
}
