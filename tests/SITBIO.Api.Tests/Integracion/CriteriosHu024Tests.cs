using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-024 (DOC-04, CP-027) contra la API y PostgreSQL reales: el
/// Administrador del Sistema inicia sesión de verdad y el resultado se comprueba en la base. Cada
/// prueba usa su propia institución con el plan fijado de forma explícita (RN-009).
/// </summary>
public class CriteriosHu024Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/tenant/actual";

    // ---------- CA-HU-024-01 ----------

    /// <summary>
    /// Los días quedan guardados en la institución, que es de donde los leerán las alertas de
    /// vencimiento (RF-020). El cálculo de las alertas llega con HU-020 (Bloque 2).
    /// </summary>
    [FactIntegracion]
    public async Task CA_HU_024_01_AlGuardarLosDiasDeAnticipacion_QuedanEnLaInstitucion()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var nit = NitNuevo();

        var respuesta = await admin.PutAsJsonAsync(Ruta, new { nombre = "Clínica San Rafael", nit, diasAnticipacionAlerta = 45 });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(45, (await Cuerpo(respuesta)).GetProperty("diasAnticipacionAlerta").GetInt32());
        Assert.Equal(45, (await Cuerpo(await admin.GetAsync(Ruta))).GetProperty("diasAnticipacionAlerta").GetInt32());
        var guardado = await api.LeerTenantAsync(tenant);
        Assert.Equal(45, guardado.DiasAnticipacionAlerta);
        Assert.Equal("Clínica San Rafael", guardado.Nombre);
        Assert.Equal(nit, guardado.Nit);
    }

    [FactIntegracion]
    public async Task CA_HU_024_01_DiasFueraDeRango_Responde400SinCambiarNada()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var antes = (await api.LeerTenantAsync(tenant)).DiasAnticipacionAlerta;

        foreach (var dias in new[] { 6, 91 })
        {
            var respuesta = await admin.PutAsJsonAsync(Ruta, new { nombre = "Clínica San Rafael", nit = NitNuevo(), diasAnticipacionAlerta = dias });

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.True((await Cuerpo(respuesta)).GetProperty("errors").TryGetProperty("diasAnticipacionAlerta", out _));
        }

        Assert.Equal(antes, (await api.LeerTenantAsync(tenant)).DiasAnticipacionAlerta);
    }

    // ---------- CA-HU-024-02 ----------

    [FactIntegracion]
    public async Task CA_HU_024_02_ElAdministradorSoloVeYEditaSuPropiaInstitucion()
    {
        var (propio, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var (ajeno, _) = await InstitucionConAdministrador("Hospital", 14, 3000, 100);
        var ajenoAntes = await api.LeerTenantAsync(ajeno);

        var consulta = await Cuerpo(await admin.GetAsync(Ruta));
        Assert.Equal(propio, Guid.Parse(consulta.GetProperty("id").GetString()!));

        // El cuerpo no elige la institución (RN-008) ni cambia el plan: eso lo hace el proveedor.
        var respuesta = await admin.PutAsJsonAsync(Ruta, new
        {
            id = ajeno,
            tenantId = ajeno,
            nombre = "Clínica Propia",
            nit = NitNuevo(),
            diasAnticipacionAlerta = 20,
            plan = "Red de Salud",
            maxEquipos = 99999,
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var propioDespues = await api.LeerTenantAsync(propio);
        Assert.Equal(20, propioDespues.DiasAnticipacionAlerta);
        Assert.Equal("Clínica", propioDespues.Plan);
        Assert.Equal(1000, propioDespues.MaxEquipos);

        var ajenoDespues = await api.LeerTenantAsync(ajeno);
        Assert.Equal(ajenoAntes.Nombre, ajenoDespues.Nombre);
        Assert.Equal(ajenoAntes.Nit, ajenoDespues.Nit);
        Assert.Equal(ajenoAntes.DiasAnticipacionAlerta, ajenoDespues.DiasAnticipacionAlerta);

        // No hay ruta para abrir otra institución por su id.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/tenant/{ajeno}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/tenant/{ajeno}", new { diasAnticipacionAlerta = 30 })).StatusCode);
    }

    [FactIntegracion]
    public async Task CA_HU_024_02_OtrosRolesReciben403YSinToken401()
    {
        var (tenant, _) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var antes = await api.LeerTenantAsync(tenant);

        foreach (var rol in new[] { CodigosRol.IngenieroBiomedico, CodigosRol.GerenteBiomedico, CodigosRol.TecnicoMantenimiento })
        {
            var cliente = await SesionDe(tenant, rol);

            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync(Ruta)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync(Ruta, new { nombre = "Intento", nit = NitNuevo(), diasAnticipacionAlerta = 10 })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Ruta)).StatusCode);
        var despues = await api.LeerTenantAsync(tenant);
        Assert.Equal(antes.Nombre, despues.Nombre);
        Assert.Equal(antes.DiasAnticipacionAlerta, despues.DiasAnticipacionAlerta);
    }

    // ---------- CA-HU-024-03 ----------

    [FactIntegracion]
    public async Task CA_HU_024_03_MuestraPlanVigenciaYConsumo_ConAvisoDesdeEl80PorCiento()
    {
        // Consultorio: 3 usuarios de gestión, 300 equipos y 5 GB.
        var (tenant, admin) = await InstitucionConAdministrador("Consultorio", 3, 300, 5);
        var vigencia = new DateOnly(2027, 6, 30);
        await api.FijarVigenciaAsync(tenant, vigencia);
        await api.CrearUsuarioAsync(tenant, api.Correo($"ing-{tenant:N}"[..20]), CodigosRol.IngenieroBiomedico);
        await api.CrearUsuarioAsync(tenant, api.Correo($"ger-{tenant:N}"[..20]), CodigosRol.GerenteBiomedico);
        await api.CrearUsuarioAsync(tenant, api.Correo($"cli-{tenant:N}"[..20]), CodigosRol.PersonalClinico); // AC-04 no cuenta.
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Tensiómetro", ClasesRiesgo.I, true, 12);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Urgencias");
        await api.LlenarEquiposAsync(tenant, tipo, ubicacion, 239, $"C24-{tenant:N}"[..12]);

        var antes = await Cuerpo(await admin.GetAsync(Ruta));

        Assert.Equal("Consultorio", antes.GetProperty("plan").GetString());
        Assert.Equal("2027-06-30", antes.GetProperty("vigenteHasta").GetString());
        var usuarios = antes.GetProperty("consumo").GetProperty("usuariosGestion");
        Assert.Equal(3, usuarios.GetProperty("usados").GetDecimal());
        Assert.Equal(3, usuarios.GetProperty("maximo").GetInt32());
        Assert.True(usuarios.GetProperty("alerta").GetBoolean());
        Assert.True(usuarios.GetProperty("limiteAlcanzado").GetBoolean());
        var equipos = antes.GetProperty("consumo").GetProperty("equiposActivos");
        Assert.Equal(239, equipos.GetProperty("usados").GetDecimal());
        Assert.Equal(79, equipos.GetProperty("porcentaje").GetInt32());
        Assert.False(equipos.GetProperty("alerta").GetBoolean());
        var almacenamiento = antes.GetProperty("consumo").GetProperty("almacenamientoGb");
        Assert.Equal(5, almacenamiento.GetProperty("maximo").GetInt32());
        Assert.False(almacenamiento.GetProperty("alerta").GetBoolean());
        Assert.True(antes.GetProperty("consumo").GetProperty("hayAlerta").GetBoolean());

        // Con el equipo 240 de 300 (80 %) se enciende el aviso de equipos.
        await api.LlenarEquiposAsync(tenant, tipo, ubicacion, 1, $"C24B-{tenant:N}"[..12]);
        var despues = (await Cuerpo(await admin.GetAsync(Ruta))).GetProperty("consumo").GetProperty("equiposActivos");

        Assert.Equal(80, despues.GetProperty("porcentaje").GetInt32());
        Assert.True(despues.GetProperty("alerta").GetBoolean());
        Assert.False(despues.GetProperty("limiteAlcanzado").GetBoolean());
    }

    private async Task<(Guid Tenant, HttpClient Administrador)> InstitucionConAdministrador(
        string plan, int maxUsuariosGestion, int maxEquipos, int maxAlmacenamientoGb)
    {
        var tenant = await api.CrearTenantAsync(plan, maxUsuariosGestion, maxEquipos, maxAlmacenamientoGb);
        return (tenant, await SesionDe(tenant, CodigosRol.AdministradorSistema));
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    /// <summary>NIT con el formato 900.000.000-0, distinto en cada llamada (es único en SITBIO).</summary>
    private static string NitNuevo() =>
        $"9{Random.Shared.Next(0, 100):00}.{Random.Shared.Next(0, 1000):000}.{Random.Shared.Next(0, 1000):000}-{Random.Shared.Next(0, 10)}";

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
