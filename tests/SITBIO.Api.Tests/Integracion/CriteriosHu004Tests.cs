using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Ordenes;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-004 (DOC-04; CP-007) contra la API, PostgreSQL y MongoDB reales: el
/// Personal Clínico reporta, el Ingeniero Biomédico asigna con API-OT y el Técnico ve la orden en su
/// panel con <c>?asignadas=me</c>. Cada prueba usa su propia institución.
/// </summary>
public class CriteriosHu004Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/ordenes";
    private const string RutaTiquetes = "/api/v1/tiquetes";
    private const string FallaNoEnciende = "No enciende";
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Institucion(
        Guid Tenant,
        HttpClient Ingeniero,
        Guid IngenieroId,
        HttpClient Clinico,
        HttpClient Tecnico,
        Guid TecnicoId,
        Guid Equipo);

    // ---------- CA-HU-004-01 (CP-007) ----------

    [FactIntegracion(requiereMongo: true)]
    public async Task CA_HU_004_01_AsignarATecnicoActivo_ElTiquetePasaAAsignadoYLaOrdenLlegaAlPanelDelTecnico()
    {
        var i = await InstitucionConEquipo();
        var tiquete = await Reportar(i.Clinico, i.Equipo, Criticidades.Critica);
        var codigo = tiquete.GetProperty("codigo").GetString()!;
        var programada = Hoy.AddDays(2);

        var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = i.TecnicoId, fechaProgramada = programada });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var orden = await Cuerpo(respuesta);
        var id = orden.GetProperty("id").GetGuid();
        Assert.Equal($"{Ruta}/{id}", respuesta.Headers.Location!.OriginalString);
        Assert.Equal((EstadosOrden.Asignada, TiposMantenimiento.Correctivo, Criticidades.Critica), (Texto(orden, "estado"), Texto(orden, "tipoMantenimiento"), Texto(orden, "prioridad")));
        Assert.Equal((codigo, "BOM-01", i.TecnicoId, i.IngenieroId), (Texto(orden, "codigoTiquete"), Texto(orden, "placaInventario"), orden.GetProperty("tecnicoId").GetGuid(), orden.GetProperty("asignadoPorId").GetGuid()));
        Assert.Equal(programada.ToString("yyyy-MM-dd"), Texto(orden, "fechaProgramada"));

        // El tiquete pasa a «asignado» y el Personal Clínico lo ve en su seguimiento, con el cambio a nombre del Ingeniero.
        Assert.Equal(EstadosTiquete.Asignado, Texto(await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}")), "estado"));
        var ultimo = (await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}/historial"))).EnumerateArray().Last();
        Assert.Equal((EstadosTiquete.Recibido, EstadosTiquete.Asignado, i.IngenieroId), (Texto(ultimo, "estadoAnterior"), Texto(ultimo, "nuevoEstado"), ultimo.GetProperty("usuarioId").GetGuid()));

        // La orden se habilita en el panel del técnico.
        var panel = await Cuerpo(await i.Tecnico.GetAsync($"{Ruta}?asignadas=me"));
        Assert.Equal([id], panel.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await i.Tecnico.GetAsync($"{Ruta}/{id}")).StatusCode);

        // La asignación queda en la bitácora sin datos personales.
        var evento = Assert.Single(await api.LeerBitacoraAsync(i.Tenant), e => e["entidad"].AsString == GestionOrdenes.Entidad);
        Assert.Equal((AccionesAuditoria.Crear, id.ToString(), i.IngenieroId.ToString()), (evento["accion"].AsString, evento["entidad_id"].AsString, evento["usuario_id"].AsString));
        Assert.Equal((codigo, Criticidades.Critica), (evento["detalle"]["tiquete"].AsString, evento["detalle"]["prioridad"].AsString));
    }

    [FactIntegracion]
    public async Task CA_HU_004_01_UnTiqueteYaAsignado_NoRecibeOtraOrden()
    {
        var i = await InstitucionConEquipo();
        var tiquete = (await Reportar(i.Clinico, i.Equipo, Criticidades.Media)).GetProperty("id").GetGuid();
        var (_, otroTecnico) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        Assert.Equal(HttpStatusCode.Created, (await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete, tecnicoId = i.TecnicoId })).StatusCode);

        var segunda = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete, tecnicoId = otroTecnico });

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal(GestionOrdenes.CodigoTiqueteAsignado, Texto(await Cuerpo(segunda), "codigo"));
        Assert.Equal(1, (await Cuerpo(await i.Ingeniero.GetAsync(Ruta))).GetProperty("total").GetInt32());
    }

    // ---------- CA-HU-004-02 (CP-007, RN-003) ----------

    [FactIntegracion]
    public async Task CA_HU_004_02_AsignarAUnInactivoOSinRolDeTecnico_SeRechazaYElTiqueteSigueRecibido()
    {
        var i = await InstitucionConEquipo();
        var tiquete = await Reportar(i.Clinico, i.Equipo, Criticidades.Alta);
        var codigo = tiquete.GetProperty("codigo").GetString()!;
        var (admin, _) = await SesionDe(i.Tenant, CodigosRol.AdministradorSistema);
        var (_, inactivo) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/usuarios/{inactivo}/estado", new { accion = "desactivar" })).StatusCode);
        var (_, clinico) = await SesionDe(i.Tenant, CodigosRol.PersonalClinico);
        var deOtraInstitucion = await api.CrearUsuarioAsync(api.TenantB, api.Correo($"tecnico-b-{Guid.NewGuid():N}"), CodigosRol.TecnicoMantenimiento);

        foreach (var candidato in new[] { inactivo, clinico, i.IngenieroId, deOtraInstitucion })
        {
            var respuesta = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete.GetProperty("id").GetGuid(), tecnicoId = candidato });

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal(GestionOrdenes.MensajeRn003, (await Cuerpo(respuesta)).GetProperty("errors").GetProperty("tecnicoId")[0].GetString());
        }

        Assert.Equal(EstadosTiquete.Recibido, Texto(await Cuerpo(await i.Clinico.GetAsync($"{RutaTiquetes}/{codigo}")), "estado"));
        Assert.Equal(0, (await Cuerpo(await i.Ingeniero.GetAsync(Ruta))).GetProperty("total").GetInt32());
    }

    // ---------- Bandeja UI-011 ----------

    [FactIntegracion]
    public async Task Bandeja_PriorizaPorCriticidad_YElPanelMuestraTecnicosActivosConSuCarga()
    {
        var i = await InstitucionConEquipo();
        var (_, otroTecnico) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var (admin, _) = await SesionDe(i.Tenant, CodigosRol.AdministradorSistema);
        var (_, inactivo) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/usuarios/{inactivo}/estado", new { accion = "desactivar" })).StatusCode);

        var baja = Texto(await Reportar(i.Clinico, i.Equipo, Criticidades.Baja), "codigo");
        var critica = Texto(await Reportar(i.Clinico, i.Equipo, Criticidades.Critica), "codigo");
        var media = await Reportar(i.Clinico, i.Equipo, Criticidades.Media);
        var alta = Texto(await Reportar(i.Clinico, i.Equipo, Criticidades.Alta), "codigo");
        Assert.Equal(HttpStatusCode.Created, (await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = media.GetProperty("id").GetGuid(), tecnicoId = otroTecnico })).StatusCode);

        // Solo los recibidos, de lo más crítico a lo menos crítico.
        var bandeja = await Cuerpo(await i.Ingeniero.GetAsync($"{RutaTiquetes}?estado=recibido&orden=criticidad"));
        Assert.Equal([critica, alta, baja], bandeja.GetProperty("items").EnumerateArray().Select(t => Texto(t, "codigo")));

        // El panel: solo técnicos activos, del menos al más ocupado.
        var tecnicos = (await Cuerpo(await i.Ingeniero.GetAsync($"{Ruta}/tecnicos"))).EnumerateArray()
            .Select(t => (t.GetProperty("id").GetGuid(), t.GetProperty("ordenesAbiertas").GetInt32())).ToList();
        Assert.Equal([(i.TecnicoId, 0), (otroTecnico, 1)], tecnicos);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.GetAsync($"{Ruta}/tecnicos")).StatusCode);
    }

    // ---------- Permisos y aislamiento ----------

    [FactIntegracion]
    public async Task Permisos_SoloElIngenieroAsigna_YElTecnicoSoloVeLasSuyas()
    {
        var i = await InstitucionConEquipo();
        var (otroTecnico, otroTecnicoId) = await SesionDe(i.Tenant, CodigosRol.TecnicoMantenimiento);
        var (regulador, _) = await SesionDe(i.Tenant, CodigosRol.EnteRegulador);
        var tiquete = (await Reportar(i.Clinico, i.Equipo, Criticidades.Baja)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await i.Tecnico.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete, tecnicoId = i.TecnicoId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await regulador.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete, tecnicoId = i.TecnicoId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.Clinico.GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Ruta)).StatusCode);

        var creada = await i.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiquete, tecnicoId = otroTecnicoId });
        var id = (await Cuerpo(creada)).GetProperty("id").GetGuid();

        // El otro técnico la ve; el técnico al que no se asignó no la lista ni la abre, aunque no pida ?asignadas=me.
        Assert.Equal(1, (await Cuerpo(await otroTecnico.GetAsync(Ruta))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Cuerpo(await i.Tecnico.GetAsync(Ruta))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await i.Tecnico.GetAsync($"{Ruta}/{id}")).StatusCode);

        // El Ente Regulador consulta todas en solo lectura.
        Assert.Equal(HttpStatusCode.OK, (await regulador.GetAsync($"{Ruta}/{id}")).StatusCode);
    }

    [FactIntegracion]
    public async Task OtraInstitucion_NoAsignaNiVeTiquetesUOrdenesAjenos()
    {
        var a = await InstitucionConEquipo();
        var b = await InstitucionConEquipo();
        var tiqueteA = (await Reportar(a.Clinico, a.Equipo, Criticidades.Alta)).GetProperty("id").GetGuid();
        var ordenA = (await Cuerpo(await a.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiqueteA, tecnicoId = a.TecnicoId }))).GetProperty("id").GetGuid();
        var tiqueteB = (await Reportar(b.Clinico, b.Equipo, Criticidades.Alta)).GetProperty("id").GetGuid();

        // El Ingeniero de B no asigna el tiquete de A ni a un técnico de A.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiqueteA, tecnicoId = b.TecnicoId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Ingeniero.PostAsJsonAsync(Ruta, new { tiqueteId = tiqueteB, tecnicoId = a.TecnicoId })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await b.Ingeniero.GetAsync($"{Ruta}/{ordenA}")).StatusCode);
        Assert.Equal(0, (await Cuerpo(await b.Ingeniero.GetAsync(Ruta))).GetProperty("total").GetInt32());
    }

    private async Task<Institucion> InstitucionConEquipo()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var (ingeniero, ingenieroId) = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var (clinico, _) = await SesionDe(tenant, CodigosRol.PersonalClinico);
        var (tecnico, tecnicoId) = await SesionDe(tenant, CodigosRol.TecnicoMantenimiento);
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
        return new Institucion(tenant, ingeniero, ingenieroId, clinico, tecnico, tecnicoId, (await Cuerpo(registro)).GetProperty("id").GetGuid());
    }

    private async Task<(HttpClient Cliente, Guid UsuarioId)> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        var id = await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return (await api.IniciarSesionAsync(correo), id);
    }

    private static async Task<JsonElement> Reportar(HttpClient cliente, Guid equipo, string criticidad)
    {
        var respuesta = await cliente.PostAsJsonAsync(RutaTiquetes, new { equipoId = equipo, tipoFalla = FallaNoEnciende, descripcion = "No prende al conectarla.", criticidad });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return await Cuerpo(respuesta);
    }

    private static string? Texto(JsonElement elemento, string propiedad) => elemento.GetProperty(propiedad).GetString();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
