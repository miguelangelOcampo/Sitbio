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
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// API completa contra PostgreSQL real (script 01 + V002..V006). Crea dos tenants (y los
/// que pidan las pruebas) con usuarios de clave conocida y borra todo al terminar. Si hay
/// MongoDB de pruebas, la bitácora va a una base temporal con el validador real (02 + V007). Sin la variable de entorno no
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

    /// <summary>Eventos de la bitácora de un tenant tal como quedaron en MongoDB, del más reciente al más antiguo.</summary>
    public async Task<List<BsonDocument>> LeerBitacoraAsync(Guid tenant)
    {
        var coleccion = new MongoClient(_cadenaMongo).GetDatabase(BaseMongo).GetCollection<BsonDocument>("bitacora_auditoria");
        return await coleccion.Find(new BsonDocument("tenant_id", tenant.ToString())).Sort(new BsonDocument("fecha_hora", -1)).ToListAsync();
    }

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
            // Misma validación que en desarrollo: se copia el validador de sitbio_dev (02 + V007).
            var cliente = new MongoClient(_cadenaMongo);
            var origen = await (await cliente.GetDatabase("sitbio_dev").ListCollectionsAsync(
                new ListCollectionsOptions { Filter = new BsonDocument("name", "bitacora_auditoria") })).SingleAsync();
            await cliente.GetDatabase(BaseMongo).RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "create", "bitacora_auditoria" },
                { "validator", origen["options"]["validator"] },
                { "validationLevel", "strict" },
                { "validationAction", "error" },
            });
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cadena))
        {
            Guid[] tenants = [TenantA, TenantB, .. _tenantsAdicionales];
            await EjecutarSqlAsync(async contexto =>
            {
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
