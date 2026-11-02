using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Criterios de aceptación de HU-011 (DOC-04) contra la API y PostgreSQL reales: el
/// Administrador inicia sesión de verdad y el resultado se comprueba en la base. Cada
/// prueba usa su propio tenant con el plan fijado de forma explícita (RN-009).
/// </summary>
public class CriteriosHu011Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string Ruta = "/api/v1/usuarios";

    [FactIntegracion]
    public async Task CA_HU_011_01_AdministradorCreaUsuario_ConHashBCryptYClaveTemporalQueDebeCambiar()
    {
        var (_, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var correo = api.Correo("nuevo-tecnico");

        var respuesta = await admin.PostAsJsonAsync(Ruta, Datos(correo, CodigosRol.TecnicoMantenimiento));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        var clave = cuerpo.GetProperty("claveTemporal").GetString()!;
        var usuarioId = Guid.Parse(cuerpo.GetProperty("usuario").GetProperty("id").GetString()!);
        var hash = await api.LeerUsuarioAsync(usuarioId, u => u.ContrasenaHash);
        Assert.StartsWith("$2", hash);
        Assert.Contains($"${HasherBCrypt.Costo}$", hash);
        Assert.DoesNotContain(clave, hash);
        Assert.True(await api.LeerUsuarioAsync(usuarioId, u => u.CambioContrasenaObligatorio));

        var (estado, login) = await api.LoginAsync(correo, clave);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.True(login.GetProperty("debeCambiarClave").GetBoolean());
        Assert.Equal("AC-03", login.GetProperty("rol").GetString());
    }

    [FactIntegracion]
    public async Task CA_HU_011_02_IngenieroYTecnico_Reciben403YNoCambiaNada()
    {
        var (tenant, _) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var objetivo = await api.CrearUsuarioAsync(tenant, api.Correo("objetivo"), CodigosRol.TecnicoMantenimiento);
        var hashOriginal = await api.LeerUsuarioAsync(objetivo, u => u.ContrasenaHash);
        var rolOriginal = await api.LeerUsuarioAsync(objetivo, u => u.RolId);
        var usuariosAntes = await api.ContarUsuariosAsync(tenant);

        foreach (var rol in new[] { CodigosRol.IngenieroBiomedico, CodigosRol.TecnicoMantenimiento })
        {
            var correo = api.Correo($"sin-permiso-{rol}");
            await api.CrearUsuarioAsync(tenant, correo, rol);
            usuariosAntes++;
            var cliente = await api.IniciarSesionAsync(correo);

            var respuestas = new[]
            {
                await cliente.GetAsync(Ruta),
                await cliente.PostAsJsonAsync(Ruta, Datos(api.Correo($"intruso-{rol}"), CodigosRol.TecnicoMantenimiento)),
                await cliente.PutAsJsonAsync($"{Ruta}/{objetivo}", Datos(api.Correo("objetivo"), CodigosRol.AdministradorSistema)),
                await cliente.PatchAsJsonAsync($"{Ruta}/{objetivo}/estado", new { accion = "desactivar" }),
                await cliente.PostAsync($"{Ruta}/{objetivo}/clave-temporal", null),
            };

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        }

        Assert.Equal(usuariosAntes, await api.ContarUsuariosAsync(tenant));
        Assert.True(await api.LeerUsuarioAsync(objetivo, u => u.EstadoActivo));
        Assert.Equal(hashOriginal, await api.LeerUsuarioAsync(objetivo, u => u.ContrasenaHash));
        Assert.Equal(rolOriginal, await api.LeerUsuarioAsync(objetivo, u => u.RolId));
    }

    [FactIntegracion]
    public async Task CA_HU_011_02_SinToken_Responde401()
    {
        var respuesta = await api.CreateClient().PostAsJsonAsync(Ruta, Datos(api.Correo("anonimo"), CodigosRol.TecnicoMantenimiento));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [FactIntegracion]
    public async Task CA_HU_011_03_PlanConsultorioLleno_BloqueaGestionPeroNoAc04NiAc07()
    {
        // Consultorio: 3 usuarios de gestión. Administrador + Ingeniero + Gerente = cupo lleno.
        var (tenant, admin) = await InstitucionConAdministrador("Consultorio", 3, 300, 5);
        await api.CrearUsuarioAsync(tenant, api.Correo("c-ingeniero"), CodigosRol.IngenieroBiomedico);
        await api.CrearUsuarioAsync(tenant, api.Correo("c-gerente"), CodigosRol.GerenteBiomedico);

        var rechazo = await admin.PostAsJsonAsync(Ruta, Datos(api.Correo("c-tecnico"), CodigosRol.TecnicoMantenimiento));

        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        var cuerpo = await Cuerpo(rechazo);
        Assert.Equal(GestionUsuarios.CodigoLimitePlan, cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("plan Consultorio", cuerpo.GetProperty("detail").GetString());
        Assert.Equal(3, await api.ContarUsuariosAsync(tenant));

        // AC-04 (ilimitado) y AC-07 (temporal) no cuentan para el límite (RN-009).
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Ruta, Datos(api.Correo("c-solicitante"), CodigosRol.PersonalClinico))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Ruta, Datos(api.Correo("c-regulador"), CodigosRol.EnteRegulador))).StatusCode);
    }

    [FactIntegracion]
    public async Task CA_HU_011_03_DesactivarLiberaCupoYReactivarConCupoLlenoResponde409()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Consultorio", 3, 300, 5);
        var ingeniero = await api.CrearUsuarioAsync(tenant, api.Correo("r-ingeniero"), CodigosRol.IngenieroBiomedico);
        await api.CrearUsuarioAsync(tenant, api.Correo("r-gerente"), CodigosRol.GerenteBiomedico);

        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"{Ruta}/{ingeniero}/estado", new { accion = "desactivar" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Ruta, Datos(api.Correo("r-tecnico"), CodigosRol.TecnicoMantenimiento))).StatusCode);

        var reactivar = await admin.PatchAsJsonAsync($"{Ruta}/{ingeniero}/estado", new { accion = "reactivar" });

        Assert.Equal(HttpStatusCode.Conflict, reactivar.StatusCode);
        Assert.Equal(GestionUsuarios.CodigoLimitePlan, (await Cuerpo(reactivar)).GetProperty("codigo").GetString());
        Assert.False(await api.LeerUsuarioAsync(ingeniero, u => u.EstadoActivo));
    }

    [FactIntegracion]
    public async Task Aislamiento_AdministradorNoVeNiModificaUsuariosDeOtroTenant()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var propio = await api.CrearUsuarioAsync(tenant, api.Correo("i-propio"), CodigosRol.TecnicoMantenimiento);
        var ajeno = await api.CrearUsuarioAsync(api.TenantB, api.Correo("i-ajeno"), CodigosRol.TecnicoMantenimiento);
        var hashAjeno = await api.LeerUsuarioAsync(ajeno, u => u.ContrasenaHash);

        var respuestas = new[]
        {
            await admin.GetAsync($"{Ruta}/{ajeno}"),
            await admin.PutAsJsonAsync($"{Ruta}/{ajeno}", Datos(api.Correo("i-ajeno"), CodigosRol.AdministradorSistema)),
            await admin.PatchAsJsonAsync($"{Ruta}/{ajeno}/estado", new { accion = "desactivar" }),
            await admin.PostAsync($"{Ruta}/{ajeno}/clave-temporal", null),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.True(await api.LeerUsuarioAsync(ajeno, u => u.EstadoActivo));
        Assert.Equal(hashAjeno, await api.LeerUsuarioAsync(ajeno, u => u.ContrasenaHash));

        var lista = await Cuerpo(await admin.GetAsync(Ruta));
        var ids = lista.GetProperty("items").EnumerateArray().Select(u => Guid.Parse(u.GetProperty("id").GetString()!)).ToList();
        Assert.Equal(2, lista.GetProperty("total").GetInt32());
        Assert.Contains(propio, ids);
        Assert.DoesNotContain(ajeno, ids);
    }

    [FactIntegracion]
    public async Task CorreoDeOtraInstitucion_Responde409SinRevelarElTenant()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var correo = api.Correo("c-compartido");
        await api.CrearUsuarioAsync(api.TenantB, correo, CodigosRol.TecnicoMantenimiento);
        var usuariosAntes = await api.ContarUsuariosAsync(tenant);

        var respuesta = await admin.PostAsJsonAsync(Ruta, Datos(correo.ToUpperInvariant(), CodigosRol.TecnicoMantenimiento));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(GestionUsuarios.CodigoCorreoEnUso, cuerpo.GetProperty("codigo").GetString());
        var texto = cuerpo.GetRawText();
        Assert.DoesNotContain(api.TenantB.ToString(), texto);
        Assert.DoesNotContain("Prueba", texto);
        Assert.Equal(usuariosAntes, await api.ContarUsuariosAsync(tenant));
    }

    [FactIntegracion]
    public async Task CA_HU_001_03_SoloElAdministradorDesbloquea_YElUsuarioVuelveAEntrar()
    {
        var (tenant, admin) = await InstitucionConAdministrador("Clínica", 7, 1000, 25);
        var correo = api.Correo("d-tecnico");
        var usuarioId = await api.CrearUsuarioAsync(tenant, correo, CodigosRol.TecnicoMantenimiento);
        for (var i = 1; i <= Usuario.MaximoIntentosFallidos; i++)
        {
            await api.LoginAsync(correo, "clave-errada");
        }

        Assert.Equal("MSG-002", (await api.LoginAsync(correo, ApiConBaseRealFixture.Clave)).Cuerpo.GetProperty("codigo").GetString());

        var respuesta = await admin.PatchAsJsonAsync($"{Ruta}/{usuarioId}/estado", new { accion = "desbloquear" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(UsuarioDto.Activo, (await Cuerpo(respuesta)).GetProperty("estado").GetString());
        Assert.False(await api.LeerUsuarioAsync(usuarioId, u => u.EstadoBloqueo));
        Assert.Equal(0, await api.LeerUsuarioAsync(usuarioId, u => (int)u.IntentosFallidos));
        Assert.Equal(HttpStatusCode.OK, (await api.LoginAsync(correo, ApiConBaseRealFixture.Clave)).Estado);
    }

    private async Task<(Guid Tenant, HttpClient Administrador)> InstitucionConAdministrador(
        string plan, int maxUsuariosGestion, int maxEquipos, int maxAlmacenamientoGb)
    {
        var tenant = await api.CrearTenantAsync(plan, maxUsuariosGestion, maxEquipos, maxAlmacenamientoGb);
        var correo = api.Correo($"admin-{Guid.NewGuid():N}"[..14]);
        await api.CrearUsuarioAsync(tenant, correo, CodigosRol.AdministradorSistema);
        return (tenant, await api.IniciarSesionAsync(correo));
    }

    private static object Datos(string correo, string rol) =>
        new { nombres = "Prueba", apellidos = "HU-011", correo, telefono = (string?)null, rol };

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
}
