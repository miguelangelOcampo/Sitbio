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
    public void Crear_SinTenant_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => new Usuario(Guid.Empty, 3, "Ana", "Pérez", "ana@clinica.co", "hash"));
    }
}
