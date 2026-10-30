namespace Sitbio.Application.Seguridad;

/// <summary>
/// Nombres de los claims del JWT. El token no lleva datos personales (correo ni nombre):
/// solo identificadores, el código de rol y el tenant (RN-008).
/// </summary>
public static class ClaimsSitbio
{
    public const string Usuario = "sub";
    public const string Tenant = "tenant_id";
    public const string Rol = "rol";
    public const string IdToken = "jti";
}
