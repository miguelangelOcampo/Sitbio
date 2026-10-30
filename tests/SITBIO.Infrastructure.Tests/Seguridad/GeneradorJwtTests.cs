using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Seguridad;

namespace Sitbio.Infrastructure.Tests.Seguridad;

public class GeneradorJwtTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class TiempoFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static OpcionesJwt Opciones(string clave = "clave-de-pruebas-con-mas-de-32-bytes!!") => new()
    {
        Issuer = "sitbio-api",
        Audience = "sitbio-desktop",
        Clave = clave,
        MinutosVigencia = 30,
    };

    private static Usuario Tecnico() => new(Tenant, 3, "Ana", "Pérez", "ana.perez@demo-a.co", "hash");

    private static TokenEmitido Emitir(DateTimeOffset ahora) =>
        new GeneradorJwt(Options.Create(Opciones()), new TiempoFijo(ahora)).Generar(Tecnico(), "AC-03");

    private static Task<TokenValidationResult> Validar(string token, OpcionesJwt opciones, DateTimeOffset ahora)
    {
        var parametros = ParametrosValidacionJwt.Crear(opciones);
        parametros.LifetimeValidator = (antes, despues, _, _) =>
            antes <= ahora.UtcDateTime + ParametrosValidacionJwt.ToleranciaReloj
            && despues > ahora.UtcDateTime - ParametrosValidacionJwt.ToleranciaReloj;
        return new JsonWebTokenHandler().ValidateTokenAsync(token, parametros);
    }

    [Fact]
    public async Task Generar_TokenConClaimsYTreintaMinutosDeVigencia()
    {
        var usuario = Tecnico();
        var emitido = new GeneradorJwt(Options.Create(Opciones()), new TiempoFijo(Ahora)).Generar(usuario, "AC-03");

        var resultado = await Validar(emitido.Token, Opciones(), Ahora);

        Assert.True(resultado.IsValid);
        var token = (JsonWebToken)resultado.SecurityToken;
        Assert.Equal(usuario.Id.ToString(), token.GetClaim(ClaimsSitbio.Usuario).Value);
        Assert.Equal(Tenant.ToString(), token.GetClaim(ClaimsSitbio.Tenant).Value);
        Assert.Equal("AC-03", token.GetClaim(ClaimsSitbio.Rol).Value);
        Assert.Equal(emitido.Jti, token.Id);
        Assert.Equal(TimeSpan.FromMinutes(30), token.ValidTo - token.IssuedAt);
        Assert.Equal(Ahora.AddMinutes(30), emitido.Expiracion);
    }

    [Fact]
    public void Generar_NoIncluyeDatosPersonales()
    {
        var emitido = Emitir(Ahora);

        var cargaUtil = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(emitido.Token.Split('.')[1]));
        Assert.DoesNotContain("ana.perez", cargaUtil);
        Assert.DoesNotContain("Pérez", cargaUtil);
    }

    [Fact]
    public void Generar_CadaTokenTieneSuPropioJti()
    {
        Assert.NotEqual(Emitir(Ahora).Jti, Emitir(Ahora).Jti);
    }

    [Fact]
    public async Task Validar_ConOtraClave_Falla()
    {
        var emitido = Emitir(Ahora);

        var resultado = await Validar(emitido.Token, Opciones("otra-clave-distinta-con-mas-de-32-bytes"), Ahora);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public async Task Validar_ConOtraAudiencia_Falla()
    {
        var emitido = Emitir(Ahora);
        var opciones = Opciones();
        opciones.Audience = "otro-cliente";

        var resultado = await Validar(emitido.Token, opciones, Ahora);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public async Task Validar_TokenVencido_Falla()
    {
        var emitido = Emitir(Ahora);

        var resultado = await Validar(emitido.Token, Opciones(), Ahora.AddMinutes(31));

        Assert.False(resultado.IsValid);
    }
}
