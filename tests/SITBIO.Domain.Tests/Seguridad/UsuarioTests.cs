using Sitbio.Domain.Seguridad;

namespace Sitbio.Domain.Tests.Seguridad;

public class UsuarioTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Crear_NormalizaElCorreoYQuedaActivo()
    {
        var usuario = new Usuario(Tenant, 3, " Ana ", "Pérez", "  Ana.Perez@Clinica.CO ", "hash", "  ");

        Assert.Equal("ana.perez@clinica.co", usuario.Correo);
        Assert.Equal("Ana", usuario.Nombres);
        Assert.Null(usuario.Telefono);
        Assert.True(usuario.EstadoActivo);
        Assert.False(usuario.EstadoBloqueo);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Equal(Tenant, usuario.TenantId);
    }

    [Theory]
    [InlineData("", "Pérez", "ana@clinica.co", "hash")]
    [InlineData("Ana", " ", "ana@clinica.co", "hash")]
    [InlineData("Ana", "Pérez", "", "hash")]
    [InlineData("Ana", "Pérez", "sin-arroba", "hash")]
    [InlineData("Ana", "Pérez", "ana@clinica.co", "")]
    public void Crear_DatosObligatoriosInvalidos_LanzaExcepcion(string nombres, string apellidos, string correo, string hash)
    {
        Assert.Throws<ArgumentException>(() => new Usuario(Tenant, 3, nombres, apellidos, correo, hash));
    }

    [Fact]
    public void RegistrarIntentoFallido_AlQuintoBloqueaLaCuenta()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");

        for (var i = 1; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarIntentoFallido();
        }

        Assert.False(usuario.EstadoBloqueo);
        usuario.RegistrarIntentoFallido();
        Assert.True(usuario.EstadoBloqueo);
        Assert.False(usuario.PuedeIniciarSesion);
        Assert.Equal(Usuario.MaximoIntentosFallidos, usuario.IntentosFallidos);
    }

    [Fact]
    public void RegistrarAccesoExitoso_ReiniciaLosIntentosConsecutivos()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");
        usuario.RegistrarIntentoFallido();
        usuario.RegistrarIntentoFallido();
        var momento = DateTimeOffset.UtcNow;

        usuario.RegistrarAccesoExitoso(momento);

        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Equal(momento, usuario.UltimoAcceso);
    }

    [Fact]
    public void RegistrarAccesoExitoso_CuentaBloqueada_Lanza()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarIntentoFallido();
        }

        Assert.Throws<InvalidOperationException>(() => usuario.RegistrarAccesoExitoso(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Desbloquear_QuitaElBloqueoYReiniciaLosIntentos()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarIntentoFallido();
        }

        usuario.Desbloquear();

        Assert.False(usuario.EstadoBloqueo);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.True(usuario.PuedeIniciarSesion);
    }

    [Fact]
    public void DesactivarYReactivar_CambianSiPuedeIniciarSesion()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");

        usuario.Desactivar();
        Assert.False(usuario.PuedeIniciarSesion);

        usuario.Reactivar();
        Assert.True(usuario.PuedeIniciarSesion);
    }

    [Fact]
    public void AsignarClaveTemporal_ObligaACambiarla()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");

        usuario.AsignarClaveTemporal("hash-temporal");

        Assert.Equal("hash-temporal", usuario.ContrasenaHash);
        Assert.True(usuario.CambioContrasenaObligatorio);
    }

    [Fact]
    public void Actualizar_NormalizaYValidaComoAlCrear()
    {
        var usuario = new Usuario(Tenant, 3, "Ana", "Pérez", "ana@clinica.co", "hash");

        usuario.Actualizar(" Ana María ", "Pérez", " Ana.Maria@Clinica.CO ", "300 123 4567");
        usuario.CambiarRol(2);

        Assert.Equal("Ana María", usuario.Nombres);
        Assert.Equal("ana.maria@clinica.co", usuario.Correo);
        Assert.Equal(2, usuario.RolId);
        Assert.Throws<ArgumentException>(() => usuario.Actualizar("Ana", "", "ana@clinica.co", null));
    }

    [Fact]
    public void Crear_SinTenant_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => new Usuario(Guid.Empty, 3, "Ana", "Pérez", "ana@clinica.co", "hash"));
    }
}
