using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Seguridad;

/// <summary>Emite el JWT de una sesión (ADR-003). El jti devuelto se registra en sesion_token.</summary>
public interface IGeneradorTokens
{
    TokenEmitido Generar(Usuario usuario, string codigoRol);
}

public sealed record TokenEmitido(string Token, string Jti, DateTimeOffset Emision, DateTimeOffset Expiracion);
