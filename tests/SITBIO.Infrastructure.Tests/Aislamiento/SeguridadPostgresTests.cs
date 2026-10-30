using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// Mapeo de rol, usuario y sesion_token contra la base real (script 01 + V004):
/// los códigos de rol coinciden con el dominio y otro tenant no ve los datos.
/// </summary>
public class SeguridadPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private SitbioDbContext CrearContexto(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        return new SitbioDbContext(options, contextoUsuario);
    }

    public async Task InitializeAsync()
    {
        await using var contexto = CrearContexto(_tenantA);
        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            var nit = $"S{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit) VALUES ({tenant}, {"Prueba seguridad"}, {nit})");
        }
    }

    public async Task DisposeAsync()
    {
        await using var contexto = CrearContexto(_tenantA);
        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.sesion_token WHERE tenant_id IN ({_tenantA}, {_tenantB})");
        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.usuario WHERE tenant_id IN ({_tenantA}, {_tenantB})");
        await contexto.Database.ExecuteSqlAsync(
            $"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
    }

    [FactIntegracion(Variable)]
    public async Task Roles_TienenLosCodigosDelDominio()
    {
        await using var contexto = CrearContexto(_tenantA);

        var codigos = await contexto.Roles.OrderBy(r => r.Codigo).Select(r => r.Codigo).ToListAsync();

        Assert.Equal(CodigosRol.Todos.Order(), codigos);
    }

    [FactIntegracion(Variable)]
    public async Task GuardarUsuarioYSesion_SeLeenConSuRolYOtroTenantNoLosVe()
    {
        var emision = DateTimeOffset.UtcNow;
        Guid usuarioId;
        await using (var contextoA = CrearContexto(_tenantA))
        {
            var tecnico = await contextoA.Roles.SingleAsync(r => r.Codigo == CodigosRol.TecnicoMantenimiento);
            var usuario = new Usuario(_tenantA, tecnico.Id, "Técnico", "De Prueba", $"tecnico.{Guid.NewGuid():N}@demo-a.co", "hash-de-prueba");
            contextoA.Usuarios.Add(usuario);
            contextoA.Sesiones.Add(new SesionToken(_tenantA, usuario.Id, Guid.NewGuid().ToString(), emision, emision.AddMinutes(30)));
            await contextoA.SaveChangesAsync();
            usuarioId = usuario.Id;
        }

        await using (var contextoA = CrearContexto(_tenantA))
        {
            var leido = await contextoA.Usuarios.Include(u => u.Rol).SingleAsync(u => u.Id == usuarioId);
            Assert.Equal(CodigosRol.TecnicoMantenimiento, leido.Rol!.Codigo);
            Assert.True(leido.EstadoActivo);
            Assert.Equal(1, await contextoA.Sesiones.CountAsync(s => s.UsuarioId == usuarioId));
        }

        await using var contextoB = CrearContexto(_tenantB);
        Assert.False(await contextoB.Usuarios.AnyAsync(u => u.Id == usuarioId));
        Assert.False(await contextoB.Sesiones.AnyAsync(s => s.UsuarioId == usuarioId));
    }
}
