using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sitbio.Api.Modules.Autenticacion;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// API completa contra PostgreSQL real (script 01 + V002..V005). Crea dos tenants con
/// usuarios de clave conocida y borra todo al terminar. Sin la variable de entorno no
/// toca la base (las pruebas se omiten).
/// </summary>
public sealed class ApiConBaseRealFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Clave = "Clave-Correcta-2026";
    public const string ClaveJwt = "clave-de-pruebas-con-mas-de-32-bytes!!";

    private static readonly string HashClave = new HasherBCrypt().Hash(Clave);
    private readonly string _cadena = Environment.GetEnvironmentVariable(FactIntegracionAttribute.VariablePostgres) ?? string.Empty;
    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];

    public Guid TenantA { get; } = Guid.NewGuid();

    public Guid TenantB { get; } = Guid.NewGuid();

    public string Correo(string nombre) => $"{nombre}.{_sufijo}@demo.co";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _cadena);
        builder.UseSetting("ConnectionStrings:Mongo", "mongodb://localhost:27017");
        builder.UseSetting("Mongo:Database", "sitbio_pruebas");
        builder.UseSetting("Jwt:Clave", ClaveJwt);
        builder.UseSetting(AutenticacionEndpoints.SeccionLimite, "1000");
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
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cadena))
        {
            await EjecutarSqlAsync(async contexto =>
            {
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.sesion_token WHERE tenant_id IN ({TenantA}, {TenantB})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.usuario WHERE tenant_id IN ({TenantA}, {TenantB})");
                await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({TenantA}, {TenantB})");
            });
        }

        await base.DisposeAsync();
    }

    private async Task EjecutarSqlAsync(Func<SitbioDbContext, Task> accion)
    {
        using var alcance = Services.CreateScope();
        await accion(alcance.ServiceProvider.GetRequiredService<SitbioDbContext>());
    }
}
