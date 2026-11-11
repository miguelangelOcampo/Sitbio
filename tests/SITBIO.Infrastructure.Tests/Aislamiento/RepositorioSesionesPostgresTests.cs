using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// <see cref="RepositorioSesiones"/> contra la base real (HU-012, HU-013): el estado de la sesión
/// sale en una sola consulta con la marca de clave temporal del usuario, exige el tenant del
/// token y las sesiones vigentes que se revocan son solo las del usuario en su institución.
/// </summary>
public class RepositorioSesionesPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly DateTimeOffset _ahora = DateTimeOffset.UtcNow;
    private Usuario _usuarioA = null!;
    private Usuario _usuarioB = null!;

    private (SitbioDbContext Contexto, RepositorioSesiones Repositorio) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioSesiones(contexto));
    }

    public async Task InitializeAsync()
    {
        _usuarioA = await Preparar(_tenantA, ["jti-a-actual", "jti-a-otro"], expirada: "jti-a-vieja");
        _usuarioB = await Preparar(_tenantB, ["jti-b"], expirada: null);
    }

    private async Task<Usuario> Preparar(Guid tenant, string[] vigentes, string? expirada)
    {
        var (contexto, _) = Crear(tenant);
        await using (contexto)
        {
            var nit = $"S{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
            await contexto.Database.ExecuteSqlAsync(
                $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba sesiones"}, {nit}, {"Consultorio"}, {300})");

            var usuario = new Usuario(tenant, 3, "Ana", "Pérez", $"sesiones.{Guid.NewGuid():N}@prueba.co", "hash");
            contexto.Usuarios.Add(usuario);
            foreach (var jti in vigentes)
            {
                contexto.Sesiones.Add(new SesionToken(tenant, usuario.Id, Jti(jti, tenant), _ahora.AddMinutes(-5), _ahora.AddMinutes(25)));
            }

            if (expirada is not null)
            {
                contexto.Sesiones.Add(new SesionToken(tenant, usuario.Id, Jti(expirada, tenant), _ahora.AddHours(-2), _ahora.AddHours(-1)));
            }

            await contexto.SaveChangesAsync();
            return usuario;
        }
    }

    // sesion_token.jti admite 36 caracteres; el tenant lo hace único entre ejecuciones.
    private static string Jti(string nombre, Guid tenant) => $"{nombre}-{tenant:N}"[..36];

    public async Task DisposeAsync()
    {
        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            foreach (var tabla in new[] { "sesion_token", "usuario" })
            {
                await contexto.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM sitbio.{tabla} WHERE tenant_id IN (@a, @b)",
                    new NpgsqlParameter("a", _tenantA),
                    new NpgsqlParameter("b", _tenantB));
            }

            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    [FactIntegracion(Variable)]
    public async Task ConsultarEstado_SigueALaMarcaDeClaveTemporalDelUsuario()
    {
        var jti = Jti("jti-a-actual", _tenantA);
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(EstadoSesion.Activa, await repositorio.ConsultarEstadoAsync(_tenantA, jti, CancellationToken.None));

            var usuario = await contexto.Usuarios.SingleAsync(u => u.Id == _usuarioA.Id);
            usuario.AsignarClaveTemporal("hash-temporal");
            await contexto.SaveChangesAsync();
            Assert.Equal(EstadoSesion.ClaveTemporal, await repositorio.ConsultarEstadoAsync(_tenantA, jti, CancellationToken.None));

            usuario.CambiarContrasena("hash-nuevo");
            await contexto.SaveChangesAsync();
            Assert.Equal(EstadoSesion.Activa, await repositorio.ConsultarEstadoAsync(_tenantA, jti, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task ConsultarEstado_ConOtroTenantORevocada_EsCerrada()
    {
        var jti = Jti("jti-a-otro", _tenantA);
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            Assert.Equal(EstadoSesion.Cerrada, await repositorio.ConsultarEstadoAsync(_tenantB, jti, CancellationToken.None));

            var sesion = await contexto.Sesiones.SingleAsync(s => s.Jti == jti);
            sesion.Revocar(_ahora, MotivosRevocacion.CambioContrasena);
            await contexto.SaveChangesAsync();

            Assert.Equal(EstadoSesion.Cerrada, await repositorio.ConsultarEstadoAsync(_tenantA, jti, CancellationToken.None));
        }
    }

    [FactIntegracion(Variable)]
    public async Task VigentesDelUsuario_SinExpiradasNiDeOtraInstitucion()
    {
        var (contexto, repositorio) = Crear(_tenantA);
        await using (contexto)
        {
            var vigentes = await repositorio.VigentesDelUsuarioAsync(_usuarioA.Id, _ahora, CancellationToken.None);
            var deOtraInstitucion = await repositorio.VigentesDelUsuarioAsync(_usuarioB.Id, _ahora, CancellationToken.None);

            Assert.Equal(
                [Jti("jti-a-actual", _tenantA), Jti("jti-a-otro", _tenantA)],
                vigentes.Select(s => s.Jti).Order(StringComparer.Ordinal));
            Assert.Empty(deOtraInstitucion);
        }
    }
}
