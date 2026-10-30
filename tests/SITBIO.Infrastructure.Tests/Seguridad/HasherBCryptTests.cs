using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Seguridad;

public class HasherBCryptTests
{
    private readonly HasherBCrypt _hasher = new();

    [Fact]
    public void Hash_UsaCostoDoceYNoGuardaLaContrasena()
    {
        var hash = _hasher.Hash("Clave-Segura-2026");

        Assert.Matches(@"^\$2[aby]\$12\$", hash);
        Assert.DoesNotContain("Clave-Segura-2026", hash);
        Assert.True(hash.Length <= 100); // usuario.contrasena_hash es VARCHAR(100)
    }

    [Fact]
    public void Verificar_ContrasenaCorrectaEIncorrecta()
    {
        var hash = _hasher.Hash("Clave-Segura-2026");

        Assert.True(_hasher.Verificar("Clave-Segura-2026", hash));
        Assert.False(_hasher.Verificar("clave-segura-2026", hash));
        Assert.False(_hasher.Verificar("", hash));
    }

    [Fact]
    public void Hash_MismaContrasena_DaHashesDistintosPorLaSal()
    {
        Assert.NotEqual(_hasher.Hash("Clave-Segura-2026"), _hasher.Hash("Clave-Segura-2026"));
    }

    [Fact]
    public void Verificar_HashDanado_DevuelveFalso()
    {
        Assert.False(_hasher.Verificar("Clave-Segura-2026", "no-es-un-hash"));
    }
}
