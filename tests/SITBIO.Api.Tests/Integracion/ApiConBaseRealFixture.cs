using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using Sitbio.Api.Modules.Autenticacion;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// API completa contra PostgreSQL real (script 01 + V002..V008). Crea dos tenants (y los
/// que pidan las pruebas) con usuarios de clave conocida y borra todo al terminar. Si hay
/// MongoDB de pruebas, la bitácora, los campos dinámicos y las intervenciones van a una base temporal con los validadores reales (02 + V007). Sin la variable de entorno no
/// toca la base (las pruebas se omiten).
/// </summary>
public sealed class ApiConBaseRealFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Clave = "Clave-Correcta-2026";
    public const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";

    private static readonly string HashClave = new HasherBCrypt().Hash(Clave);
    private readonly string _cadena = Environment.GetEnvironmentVariable(FactIntegracionAttribute.VariablePostgres) ?? string.Empty;
    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];
    private readonly string _cadenaMongo = Environment.GetEnvironmentVariable(FactIntegracionAttribute.VariableMongo) ?? string.Empty;
    private readonly ConcurrentBag<Guid> _tenantsAdicionales = [];
    private int _consecutivoTenant;

    public Guid TenantA { get; } = Guid.NewGuid();

    public Guid TenantB { get; } = Guid.NewGuid();

    public string Correo(string nombre) => $"{nombre}.{_sufijo}@demo.co";

    /// <summary>Base temporal de MongoDB de este fixture; se borra al terminar.</summary>
    public string BaseMongo => $"sitbio_pruebas_api_{_sufijo}";

    /// <summary>Versiones de configuracion_campos de un tenant tal como quedaron en MongoDB, de la más reciente a la más antigua.</summary>
    public Task<List<BsonDocument>> LeerConfiguracionCamposAsync(Guid tenant) =>
        new MongoClient(_cadenaMongo).GetDatabase(BaseMongo).GetCollection<BsonDocument>("configuracion_campos")
            .Find(new BsonDocument("tenant_id", tenant.ToString()))
            .Sort(new BsonDocument("version", -1))
            .ToListAsync();

    /// <summary>Intervenciones de un tenant tal como quedaron en MongoDB (HU-005).</summary>
    public Task<List<BsonDocument>> LeerIntervencionesAsync(Guid tenant) =>
        new MongoClient(_cadenaMongo).GetDatabase(BaseMongo).GetCollection<BsonDocument>("intervenciones")
            .Find(new BsonDocument("tenant_id", tenant.ToString()))
            .ToListAsync();

    /// <summary>Marca la intervención de la orden como firmada, como lo hará la aprobación de HU-021 (RN-004).</summary>
    public Task FirmarIntervencionAsync(Guid ordenId) =>
        new MongoClient(_cadenaMongo).GetDatabase(BaseMongo).GetCollection<BsonDocument>("intervenciones")
            .UpdateOneAsync(new BsonDocument("orden_id", ordenId.ToString()), new BsonDocument("$set", new BsonDocument("cerrada_firmada", true)));

    /// <summary>Eventos de la bitácora de un tenant tal como quedaron en MongoDB, del más reciente al más antiguo.</summary>
    public async Task<List<BsonDocument>> LeerBitacoraAsync(Guid tenant)
    {
        var coleccion = new MongoClient(_cadenaMongo).GetDatabase(BaseMongo).GetCollection<BsonDocument>("bitacora_auditoria");
        return await coleccion.Find(new BsonDocument("tenant_id", tenant.ToString())).Sort(new BsonDocument("fecha_hora", -1)).ToListAsync();
    }

    /// <summary>Tiquetes de la institución con su historial, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<List<(Tiquete Tiquete, List<HistorialTiquete> Historial)>> LeerTiquetesAsync(Guid tenant)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        var tiquetes = await contexto.Tiquetes.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tenant).OrderBy(t => t.Codigo).ToListAsync();
        var historial = await contexto.HistorialTiquetes.IgnoreQueryFilters().AsNoTracking().Where(h => h.TenantId == tenant).ToListAsync();
        return tiquetes.Select(t => (t, historial.Where(h => h.TiqueteId == t.Id).ToList())).ToList();
    }

    /// <summary>Tipo de falla activo del catálogo de la institución (HU-025), que exige el reporte de fallas.</summary>
    public Task CrearTipoFallaAsync(Guid tenant, string nombre) =>
        EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.tipo_falla (tenant_id, nombre) VALUES ({tenant}, {nombre})"));

    /// <summary>
    /// Pasa un tiquete a otro estado directamente en la base y deja el cambio en su historial, como lo
    /// harán la ejecución (HU-005) y el cierre (HU-021); la asignación ya existe (HU-004). Al cerrar fija la fecha de cierre.
    /// </summary>
    public Task CambiarEstadoTiqueteAsync(Guid tenant, string codigo, Guid usuarioId, string nuevoEstado, string comentario, DateTimeOffset fecha) =>
        EjecutarSqlAsync(async contexto =>
        {
            await contexto.Database.ExecuteSqlAsync($"""
                INSERT INTO sitbio.historial_tiquete (tenant_id, tiquete_id, usuario_id, estado_anterior, nuevo_estado, comentario, fecha_cambio)
                SELECT tenant_id, id_tiquete, {usuarioId}, estado, {nuevoEstado}, {comentario}, {fecha}
                FROM sitbio.tiquete WHERE tenant_id = {tenant} AND codigo = {codigo}
                """);
            await contexto.Database.ExecuteSqlAsync($"""
                UPDATE sitbio.tiquete
                SET estado = {nuevoEstado}, fecha_cierre = CASE WHEN {nuevoEstado} = {EstadosTiquete.Cerrado} THEN {fecha} ELSE fecha_cierre END
                WHERE tenant_id = {tenant} AND codigo = {codigo}
                """);
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _cadena);
        builder.UseSetting("ConnectionStrings:Mongo", string.IsNullOrWhiteSpace(_cadenaMongo) ? "mongodb://localhost:27017" : _cadenaMongo);
        builder.UseSetting("Mongo:Database", BaseMongo);
        builder.UseSetting("Jwt:Clave", ClaveJwt);
        builder.UseSetting(AutenticacionEndpoints.SeccionLimite, "1000");
    }

    /// <summary>
    /// Crea un tenant con su plan fijado de forma explícita (RN-009: los valores por defecto
    /// de la base son los de Clínica). Se borra al terminar.
    /// </summary>
    public async Task<Guid> CrearTenantAsync(string plan, int maxUsuariosGestion, int maxEquipos, int maxAlmacenamientoGb)
    {
        var id = Guid.NewGuid();
        var nit = $"T{_sufijo}{Interlocked.Increment(ref _consecutivoTenant)}";
        _tenantsAdicionales.Add(id);
        await EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_usuarios_gestion, max_equipos, max_almacenamiento_gb) VALUES ({id}, {"Prueba " + plan}, {nit}, {plan}, {maxUsuariosGestion}, {maxEquipos}, {maxAlmacenamientoGb})"));
        return id;
    }

    /// <summary>POST /auth/login real; devuelve el estado y el cuerpo.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> LoginAsync(string correo, string clave)
    {
        var respuesta = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { correo, contrasena = clave });
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
        return (respuesta.StatusCode, cuerpo);
    }

    /// <summary>Cliente con el token que entrega el inicio de sesión real del usuario.</summary>
    public async Task<HttpClient> IniciarSesionAsync(string correo, string clave = Clave)
    {
        var (estado, cuerpo) = await LoginAsync(correo, clave);
        if (estado != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"El inicio de sesión de prueba respondió {(int)estado}.");
        }

        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cuerpo.GetProperty("token").GetString());
        return cliente;
    }

    /// <summary>Cuántos usuarios tiene el tenant, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<int> ContarUsuariosAsync(Guid tenant)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Usuarios.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenant);
    }

    /// <summary>Crea un usuario con la clave conocida en el tenant indicado.</summary>
    public async Task<Guid> CrearUsuarioAsync(Guid tenant, string correo, string codigoRol)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        var rol = await contexto.Roles.SingleAsync(r => r.Codigo == codigoRol);
        var usuario = new Usuario(tenant, rol.Id, "Prueba", "HU-001", correo, HashClave);
        alcance.ServiceProvider.GetRequiredService<IEstablecerContextoUsuario>().Establecer(tenant, usuario.Id, codigoRol);
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync();
        return usuario.Id;
    }

    /// <summary>Lee la sesión de un jti sin pasar por el filtro de tenant (solo para comprobar); null si no existe.</summary>
    public async Task<SesionToken?> LeerSesionAsync(string jti)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Sesiones.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Jti == jti);
    }

    /// <summary>
    /// JWT firmado con la clave de pruebas, emitido en <paramref name="emision"/>, y su sesión
    /// registrada (sin revocar) en el tenant <paramref name="tenantSesion"/>. Sirve para fabricar
    /// tokens vencidos o con un jti de otra institución sin depender del reloj del login.
    /// </summary>
    public async Task<string> TokenConSesionAsync(Guid usuarioId, Guid tenant, string codigoRol, DateTimeOffset emision, Guid? tenantSesion = null, Guid? usuarioSesion = null)
    {
        var jti = Guid.NewGuid().ToString();
        var expiracion = emision.AddMinutes(OpcionesJwt.MinutosMaximos);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "sitbio-api",
            Audience = "sitbio-desktop",
            IssuedAt = emision.UtcDateTime,
            NotBefore = emision.UtcDateTime,
            Expires = expiracion.UtcDateTime,
            SigningCredentials = new SigningCredentials(new OpcionesJwt { Clave = ClaveJwt }.CrearClaveFirma(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimsSitbio.Usuario] = usuarioId.ToString(),
                [ClaimsSitbio.Tenant] = tenant.ToString(),
                [ClaimsSitbio.Rol] = codigoRol,
                [ClaimsSitbio.IdToken] = jti,
            },
        });

        var tenantDeLaSesion = tenantSesion ?? tenant;
        var usuarioDeLaSesion = usuarioSesion ?? usuarioId;
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        alcance.ServiceProvider.GetRequiredService<IEstablecerContextoUsuario>().Establecer(tenantDeLaSesion, usuarioDeLaSesion, codigoRol);
        contexto.Sesiones.Add(new SesionToken(tenantDeLaSesion, usuarioDeLaSesion, jti, emision, expiracion));
        await contexto.SaveChangesAsync();
        return token;
    }

    /// <summary>Lee una columna de usuario sin pasar por el filtro de tenant (solo para comprobar).</summary>
    public async Task<T> LeerUsuarioAsync<T>(Guid usuarioId, Func<Usuario, T> campo)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        var usuario = await contexto.Usuarios.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == usuarioId);
        return campo(usuario);
    }

    /// <summary>Ubicación activa del tenant (el catálogo se gestiona con HU-025).</summary>
    public async Task<Guid> CrearUbicacionAsync(Guid tenant, string nombre)
    {
        var id = Guid.NewGuid();
        await EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.ubicacion (id_ubicacion, tenant_id, nombre) VALUES ({id}, {tenant}, {nombre})"));
        return id;
    }

    /// <summary>Tipo de equipo con sus valores regulatorios por defecto (V008).</summary>
    public async Task<Guid> CrearTipoEquipoAsync(Guid tenant, string nombre, string? clase, bool requiereCalibracion, int? meses)
    {
        var id = Guid.NewGuid();
        await EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.tipo_equipo (id_tipo_equipo, tenant_id, nombre, clase_riesgo_defecto, requiere_calibracion, periodicidad_calibracion_meses) VALUES ({id}, {tenant}, {nombre}, {clase}, {requiereCalibracion}, {(short?)meses})"));
        return id;
    }

    /// <summary>Inserta <paramref name="cantidad"/> equipos operativos de una vez (para llenar el cupo del plan, RN-009).</summary>
    public Task LlenarEquiposAsync(Guid tenant, Guid tipo, Guid ubicacion, int cantidad, string prefijo) =>
        EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO sitbio.equipo_biomedico (tenant_id, tipo_equipo_id, ubicacion_id, estado_equipo_id, numero_serie, placa_inventario, marca, modelo,
                tipo_autorizacion, registro_invima, vence_registro, clase_riesgo, requiere_calibracion, periodicidad_calibracion_meses)
            SELECT {tenant}, {tipo}, {ubicacion}, 1, {prefijo} || '-S' || g, {prefijo} || '-P' || g, 'Mindray', 'T5',
                'registro_sanitario', 'R-' || g, current_date + 365, 'I', false, NULL
            FROM generate_series(1, {cantidad}) AS g
            """));

    /// <summary>Da de baja un equipo directamente en la base (el flujo de baja llega con HU-014).</summary>
    public Task DarDeBajaAsync(Guid equipoId) =>
        EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"UPDATE sitbio.equipo_biomedico SET estado_equipo_id = {EstadosEquipo.Baja}, fecha_baja = now() WHERE id_equipo = {equipoId}"));

    /// <summary>Equipos del tenant, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<int> ContarEquiposAsync(Guid tenant)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Equipos.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant);
    }

    /// <summary>Equipo tal como quedó en la base, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<EquipoBiomedico> LeerEquipoAsync(Guid equipoId)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Equipos.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == equipoId);
    }

    /// <summary>Historial de estados del equipo tal como quedó en la base, del más reciente al más antiguo (solo para comprobar).</summary>
    public async Task<List<HistorialEstadoEquipo>> LeerHistorialAsync(Guid equipoId)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.HistorialEstados.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.EquipoId == equipoId)
            .OrderByDescending(h => h.FechaCambio)
            .ToListAsync();
    }

    /// <summary>Planes preventivos del equipo, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<List<PlanPreventivo>> LeerPlanesAsync(Guid equipoId)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.PlanesPreventivos.IgnoreQueryFilters().AsNoTracking().Where(p => p.EquipoId == equipoId).ToListAsync();
    }

    /// <summary>Institución tal como quedó en la base, sin pasar por el filtro (solo para comprobar).</summary>
    public async Task<Tenant> LeerTenantAsync(Guid tenant)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenant);
    }

    /// <summary>Fija la vigencia del plan como lo haría el proveedor al aprovisionar (ST-10).</summary>
    public Task FijarVigenciaAsync(Guid tenant, DateOnly vigenteHasta) =>
        EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"UPDATE sitbio.tenant SET vigente_hasta = {vigenteHasta} WHERE id_tenant = {tenant}"));

    public async Task<bool> ExisteSesionAsync(string jti)
    {
        using var alcance = Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        return await contexto.Sesiones.IgnoreQueryFilters().AnyAsync(s => s.Jti == jti);
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_cadena))
        {
            return;
        }

        await EjecutarSqlAsync(contexto => contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit) VALUES ({TenantA}, {"Prueba HU-001 A"}, {"HA" + _sufijo}), ({TenantB}, {"Prueba HU-001 B"}, {"HB" + _sufijo})"));

        if (!string.IsNullOrWhiteSpace(_cadenaMongo))
        {
            // Misma validación que en desarrollo: se copian los validadores de sitbio_dev (02 + V007).
            var cliente = new MongoClient(_cadenaMongo);
            foreach (var coleccion in new[] { "bitacora_auditoria", "configuracion_campos", "intervenciones" })
            {
                var origen = await (await cliente.GetDatabase("sitbio_dev").ListCollectionsAsync(
                    new ListCollectionsOptions { Filter = new BsonDocument("name", coleccion) })).SingleAsync();
                await cliente.GetDatabase(BaseMongo).RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    { "create", coleccion },
                    { "validator", origen["options"]["validator"] },
                    { "validationLevel", "strict" },
                    { "validationAction", "error" },
                });
            }

            // Una versión por número en cada institución y tipo de equipo (script 02).
            await cliente.GetDatabase(BaseMongo).GetCollection<BsonDocument>("configuracion_campos").Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    new BsonDocument { { "tenant_id", 1 }, { "tipo_equipo", 1 }, { "version", -1 } },
                    new CreateIndexOptions { Unique = true, Name = "uq_config_tenant_tipo_version" }));
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cadena))
        {
            Guid[] tenants = [TenantA, TenantB, .. _tenantsAdicionales];
            await EjecutarSqlAsync(async contexto =>
            {
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.historial_estado_equipo WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.plan_preventivo WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.orden_trabajo WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.historial_tiquete WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tiquete WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.equipo_biomedico WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.proveedor WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tipo_falla WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tipo_equipo WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.ubicacion WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.sesion_token WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.usuario WHERE tenant_id = ANY({tenants})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant = ANY({tenants})");
            });
        }

        if (!string.IsNullOrWhiteSpace(_cadenaMongo))
        {
            await new MongoClient(_cadenaMongo).DropDatabaseAsync(BaseMongo);
        }

        await base.DisposeAsync();
    }

    private async Task EjecutarSqlAsync(Func<SitbioDbContext, Task> accion)
    {
        using var alcance = Services.CreateScope();
        await accion(alcance.ServiceProvider.GetRequiredService<SitbioDbContext>());
    }
}
