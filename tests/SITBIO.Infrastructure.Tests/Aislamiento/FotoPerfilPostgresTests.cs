using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Sitbio.Application.Comun;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// J-11 (V011): la ruta de la foto de perfil se guarda en <c>usuario.foto_ruta</c> y el usuario de otra institución
/// no se alcanza ni para leerla ni para cambiarla.
/// </summary>
public class FotoPerfilPostgresTests : IAsyncLifetime
{
    private const string Variable = FactIntegracionAttribute.VariablePostgres;

    private readonly string _cadena = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _lauraA = Guid.NewGuid();
    private readonly Guid _andresB = Guid.NewGuid();

    private (SitbioDbContext Contexto, RepositorioUsuarios Usuarios) Crear(Guid tenantId)
    {
        var contextoUsuario = Substitute.For<IContextoUsuario>();
        contextoUsuario.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<SitbioDbContext>().UseNpgsql(_cadena).Options;
        var contexto = new SitbioDbContext(options, contextoUsuario);
        return (contexto, new RepositorioUsuarios(contexto, contextoUsuario));
    }

    public async Task InitializeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        foreach (var (tenant, usuario) in new[] { (_tenantA, _lauraA), (_tenantB, _andresB) })
        {
            var (contexto, _) = Crear(tenant);
            await using (contexto)
            {
                var nit = $"T{Random.Shared.NextInt64(100_000_000, 999_999_999)}";
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_equipos) VALUES ({tenant}, {"Prueba foto"}, {nit}, {"Consultorio"}, {300})");
                await contexto.Database.ExecuteSqlAsync(
                    $"INSERT INTO sitbio.usuario (id_usuario, tenant_id, rol_id, nombres, apellidos, correo, contrasena_hash) SELECT {usuario}, {tenant}, id_rol, {"Laura"}, {"Gómez"}, {$"{usuario:N}@prueba-foto.co"}, {"hash"} FROM sitbio.rol WHERE codigo = {"AC-04"}");
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_cadena.Length == 0)
        {
            return;
        }

        var (contexto, _) = Crear(_tenantA);
        await using (contexto)
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "DELETE FROM sitbio.usuario WHERE tenant_id IN (@a, @b)",
                new NpgsqlParameter("a", _tenantA),
                new NpgsqlParameter("b", _tenantB));
            await contexto.Database.ExecuteSqlAsync($"DELETE FROM sitbio.tenant WHERE id_tenant IN ({_tenantA}, {_tenantB})");
        }
    }

    [FactIntegracion(Variable)]
    public async Task J11_LaFotoSeGuardaYSeQuita_SoloDentroDeLaInstitucion()
    {
        var ruta = $"{_tenantA}/{_lauraA}/perfil-1.png";
        var (contexto, usuarios) = Crear(_tenantA);
        await using (contexto)
        {
            var (laura, _) = (await usuarios.ObtenerAsync(_lauraA, CancellationToken.None))!.Value;
            Assert.Null(laura.FotoRuta);
            laura.CambiarFoto(ruta);
            await usuarios.GuardarAsync(CancellationToken.None);
        }

        var (otro, otraInstitucion) = Crear(_tenantB);
        await using (otro)
        {
            Assert.Null(await otraInstitucion.ObtenerAsync(_lauraA, CancellationToken.None));
        }

        var (lectura, mismos) = Crear(_tenantA);
        await using (lectura)
        {
            var (laura, _) = (await mismos.ObtenerAsync(_lauraA, CancellationToken.None))!.Value;
            Assert.Equal(ruta, laura.FotoRuta);
            laura.QuitarFoto();
            await mismos.GuardarAsync(CancellationToken.None);
        }

        var (final, ultimos) = Crear(_tenantA);
        await using (final)
        {
            Assert.Null((await ultimos.ObtenerAsync(_lauraA, CancellationToken.None))!.Value.Usuario.FotoRuta);
        }
    }
}
