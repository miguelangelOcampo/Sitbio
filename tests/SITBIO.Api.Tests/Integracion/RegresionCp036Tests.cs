using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Regresión de CP-036 (RN-008, SEC-003) de punta a punta: la institución A registra un equipo y reporta una
/// falla; con cada rol de la institución B se recorren los listados y consultas de la API y ninguna respuesta
/// deja ver el equipo, el tiquete, los usuarios ni el tenant de A. Las consultas directas por id de A responden
/// como si no existieran (en la bitácora de B sí quedan esos intentos, con la ruta pedida). Complementa las pruebas de aislamiento de cada historia.
/// </summary>
public class RegresionCp036Tests(ApiConBaseRealFixture api) : IClassFixture<ApiConBaseRealFixture>
{
    private const string V1 = "/api/v1";
    private const string FallaNoEnciende = "No enciende";

    private static readonly string[] Listados =
    [
        "/equipos",
        "/tiquetes",
        "/ordenes",
        "/ordenes/tecnicos",
        "/usuarios",
        "/planes-preventivos",
        "/calibraciones",
        "/alertas",
        "/bitacora",
        "/indicadores",
        "/tenant/actual",
        "/analitica/agrupamientos",
    ];

    private static readonly string[] RolesB =
    [
        CodigosRol.AdministradorSistema,
        CodigosRol.IngenieroBiomedico,
        CodigosRol.TecnicoMantenimiento,
        CodigosRol.PersonalClinico,
        CodigosRol.GerenteBiomedico,
        CodigosRol.DirectivaInstitucional,
        CodigosRol.EnteRegulador,
    ];

    private sealed record Institucion(Guid Tenant, Guid Equipo, string Placa, string Tiquete, string CorreoIngeniero);

    [FactIntegracion(requiereMongo: true)]
    public async Task CP_036_NingunRolDeOtraInstitucion_VeLosDatosDeA_EnNingunaConsulta()
    {
        var a = await InstitucionConEquipoYFallaAsync();
        var tenantB = await api.CrearTenantAsync("Clínica", 10, 1000, 25);
        var datosDeA = new[] { a.Tenant.ToString(), a.Equipo.ToString(), a.Placa, a.CorreoIngeniero };

        var clientes = new Dictionary<string, HttpClient>();
        foreach (var rol in RolesB)
        {
            clientes[rol] = await SesionDe(tenantB, rol);
        }

        // Primero los listados: las consultas por id de A que vienen después quedan en la bitácora de B como
        // accesos denegados de sus propios usuarios, con la ruta pedida; eso es de B y no una fuga.
        foreach (var (rol, cliente) in clientes)
        {
            foreach (var ruta in Listados)
            {
                var respuesta = await cliente.GetAsync(V1 + ruta);
                Assert.True(respuesta.StatusCode != HttpStatusCode.InternalServerError, $"{rol} recibe 500 en {ruta}.");
                var cuerpo = await respuesta.Content.ReadAsStringAsync();
                foreach (var dato in datosDeA)
                {
                    Assert.False(cuerpo.Contains(dato, StringComparison.OrdinalIgnoreCase), $"{rol} ve «{dato}» de otra institución en {ruta}.");
                }
            }
        }

        foreach (var (rol, cliente) in clientes)
        {
            foreach (var ruta in new[] { $"/equipos/{a.Equipo}", $"/equipos/{a.Equipo}/linea-tiempo", $"/equipos/{a.Equipo}/hoja-vida.pdf", $"/tiquetes/{a.Tiquete}/historial" })
            {
                var respuesta = await cliente.GetAsync(V1 + ruta);
                var cuerpo = await respuesta.Content.ReadAsStringAsync();
                Assert.True(
                    respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden || !cuerpo.Contains(a.Equipo.ToString(), StringComparison.OrdinalIgnoreCase),
                    $"{rol} recibe {(int)respuesta.StatusCode} con datos de A en {ruta}.");
            }
        }
    }

    private async Task<Institucion> InstitucionConEquipoYFallaAsync()
    {
        var tenant = await api.CrearTenantAsync("Clínica", 10, 1000, 25);
        var correo = api.Correo($"ingeniero-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, CodigosRol.IngenieroBiomedico);
        var ingeniero = await api.IniciarSesionAsync(correo);
        await api.CrearTipoFallaAsync(tenant, FallaNoEnciende);

        var placa = $"CP36-{Guid.NewGuid():N}"[..14];
        var tipo = await api.CrearTipoEquipoAsync(tenant, "Monitor de signos vitales", ClasesRiesgo.IIb, true, 12);
        var ubicacion = await api.CrearUbicacionAsync(tenant, "Urgencias");
        var registro = await ingeniero.PostAsJsonAsync($"{V1}/equipos", new Dictionary<string, object?>
        {
            ["tipoEquipoId"] = tipo,
            ["ubicacionId"] = ubicacion,
            ["numeroSerie"] = $"SN-{placa}",
            ["placaInventario"] = placa,
            ["marca"] = "Mindray",
            ["modelo"] = "uMEC10",
            ["tipoAutorizacion"] = TiposAutorizacion.RegistroSanitario,
            ["registroInvima"] = "INVIMA 2020DM-0004321",
            ["venceRegistro"] = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2),
            ["claseRiesgo"] = ClasesRiesgo.IIb,
            ["requiereCalibracion"] = true,
            ["periodicidadCalibracionMeses"] = 12,
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var equipo = (await registro.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var falla = await ingeniero.PostAsJsonAsync($"{V1}/tiquetes", Falla(equipo));
        Assert.Equal(HttpStatusCode.Created, falla.StatusCode);
        var codigo = (await falla.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString()!;
        return new Institucion(tenant, equipo, placa, codigo, correo);
    }

    private static Dictionary<string, object?> Falla(Guid equipo) => new()
    {
        ["equipoId"] = equipo,
        ["tipoFalla"] = FallaNoEnciende,
        ["descripcion"] = "El monitor no enciende al conectarlo.",
        ["criticidad"] = Criticidades.Alta,
    };

    private async Task<HttpClient> SesionDe(Guid tenant, string codigoRol)
    {
        var correo = api.Correo($"{codigoRol}-{Guid.NewGuid():N}");
        await api.CrearUsuarioAsync(tenant, correo, codigoRol);
        return await api.IniciarSesionAsync(correo);
    }
}
