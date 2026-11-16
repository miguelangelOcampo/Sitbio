using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-028 (DOC-04; RF-029, RN-009) contra la API y PostgreSQL reales: la
/// solicitud sin cuenta queda en <c>solicitud_servicio</c> con su código, y el cambio de plan que no cabe
/// en el consumo actual no se guarda.
/// </summary>
public class CriteriosHu028Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/solicitudes-servicio";

    // ---------- CA-HU-028-01 ----------

    [FactIntegracion]
    public async Task CA_HU_028_01_SinCuenta_ConsultaPlanesYEnviaLaSolicitud_RecibeUnCodigoQueQuedaParaElProveedor()
    {
        var anonimo = api.CreateClient();
        var correo = api.Correo($"demo-{Guid.NewGuid():N}"[..20]);

        var planes = await Cuerpo(await anonimo.GetAsync("/api/v1/planes"));
        Assert.Equal(4, planes.GetArrayLength());

        var respuesta = await anonimo.PostAsJsonAsync(Ruta, new
        {
            tipo = "cotizacion",
            institucion = "IPS Nueva Esperanza",
            nit = "901.234.567-8",
            contacto = "Laura Ríos",
            correo,
            telefono = "6045551234",
            planSolicitado = "Hospital",
            mensaje = "Tenemos 2 400 equipos.",
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var codigo = (await Cuerpo(respuesta)).GetProperty("codigo").GetString()!;
        Assert.Matches(@"^SOL-\d{6}$", codigo);

        var guardada = await api.LeerSolicitudAsync(codigo);
        Assert.NotNull(guardada);
        Assert.Equal(TiposSolicitudServicio.Cotizacion, guardada.Tipo);
        Assert.Equal(EstadosSolicitudServicio.Recibida, guardada.Estado);
        Assert.Null(guardada.TenantId);
        Assert.Equal("IPS Nueva Esperanza", guardada.Institucion);
        Assert.Equal("Hospital", guardada.PlanSolicitado);
        Assert.Equal(correo, guardada.Correo);
    }

    [FactIntegracion]
    public async Task CA_HU_028_01_DosSolicitudesSeguidas_RecibenCodigosDistintos()
    {
        var anonimo = api.CreateClient();
        var correo = api.Correo($"dos-{Guid.NewGuid():N}"[..20]);
        object Datos() => new { tipo = "demostracion", institucion = "Clínica del Valle", nit = "900.111.222-3", contacto = "Juan", correo };

        var respuestas = await Task.WhenAll(anonimo.PostAsJsonAsync(Ruta, Datos()), anonimo.PostAsJsonAsync(Ruta, Datos()));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var codigos = await Task.WhenAll(respuestas.Select(async r => (await Cuerpo(r)).GetProperty("codigo").GetString()));
        Assert.NotEqual(codigos[0], codigos[1]);
        Assert.Equal(2, await api.ContarSolicitudesAsync(correo));
    }

    // ---------- CA-HU-028-02 ----------

    [FactIntegracion]
    public async Task CA_HU_028_02_ConsumoMayorQueElPlanElegido_NoSeEnviaEIndicaElLimite()
    {
        // Hospital con 4 usuarios de gestión (el Administrador y tres más): Consultorio solo admite 3.
        var tenant = await api.CrearTenantAsync("Hospital", 14, 3000, 100);
        var admin = await SesionDe(tenant, CodigosRol.AdministradorSistema);
        foreach (var rol in new[] { CodigosRol.IngenieroBiomedico, CodigosRol.GerenteBiomedico, CodigosRol.TecnicoMantenimiento })
        {
            await api.CrearUsuarioAsync(tenant, api.Correo($"{rol}-{Guid.NewGuid():N}"), rol);
        }

        var correo = api.Correo($"cambio-{Guid.NewGuid():N}"[..20]);
        var respuesta = await admin.PostAsJsonAsync(Ruta, new { tipo = "cambio_plan", contacto = "Admin", correo, planSolicitado = "Consultorio" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal("RN-009", cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("usuarios de gestión 4 de 3", cuerpo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await api.ContarSolicitudesAsync(correo));
    }

    [FactIntegracion]
    public async Task CA_HU_028_02_ConsumoQueCabe_SeEnviaAnombreDeLaInstitucionDelToken()
    {
        var tenant = await api.CrearTenantAsync("Consultorio", 3, 300, 5);
        var admin = await SesionDe(tenant, CodigosRol.AdministradorSistema);
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor", ClasesRiesgo.IIa, false, null);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "UCI");
        await api.LlenarEquiposAsync(tenant, tipo, ubicacion, 10, $"H28-{tenant:N}"[..12]);

        var correo = api.Correo($"sube-{Guid.NewGuid():N}"[..20]);
        var respuesta = await admin.PostAsJsonAsync(Ruta, new
        {
            tipo = "cambio_plan",
            institucion = "Otra institución",
            nit = "999.999.999-9",
            contacto = "Admin",
            correo,
            planSolicitado = "Clínica",
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var guardada = await api.LeerSolicitudAsync((await Cuerpo(respuesta)).GetProperty("codigo").GetString()!);
        var institucion = await api.LeerTenantAsync(tenant);
        Assert.NotNull(guardada);
        Assert.Equal(TiposSolicitudServicio.CambioPlan, guardada.Tipo);
        Assert.Equal(tenant, guardada.TenantId);
        Assert.Equal(institucion.Nombre, guardada.Institucion);
        Assert.Equal(institucion.Nit, guardada.Nit);
        Assert.Equal("Consultorio", institucion.Plan); // El plan lo cambia el proveedor al atender la solicitud.
    }

    [FactIntegracion]
    public async Task CA_HU_028_02_SoloElAdministradorPideElCambio()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 7, 1000, 25);
        var ingeniero = await SesionDe(tenant, CodigosRol.IngenieroBiomedico);
        var correo = api.Correo($"ing-{Guid.NewGuid():N}"[..20]);
        var cuerpo = new { tipo = "cambio_plan", contacto = "Ing", correo, planSolicitado = "Hospital" };

        Assert.Equal(HttpStatusCode.Forbidden, (await ingeniero.PostAsJsonAsync(Ruta, cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync(Ruta, cuerpo)).StatusCode);
        Assert.Equal(0, await api.ContarSolicitudesAsync(correo));
    }

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
