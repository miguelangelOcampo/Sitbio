using Sitbio.Application.Comun.Errores;

namespace Sitbio.Api.Seguridad;

/// <summary>
/// Cambio obligatorio de la clave temporal (RF-012, CA-HU-013-02, RNF-003). Mientras el usuario
/// tenga una clave temporal, la API solo atiende las rutas marcadas con
/// <see cref="PermiteClaveTemporalAttribute"/> (cambiar la contraseña, renovar y cerrar sesión);
/// cualquier otra responde 403 con MSG-004. Ocultar pantallas en WPF no basta.
/// </summary>
public static class ClaveTemporal
{
    /// <summary>
    /// Claim interno que agrega la validación del token a partir de la base de datos (no viaja en
    /// el JWT): así, en cuanto el usuario cambia la contraseña, el mismo token sirve para todo.
    /// </summary>
    public const string Claim = "sitbio_clave_temporal";

    public const string Codigo = "MSG-004";
    public const string Mensaje = "Por seguridad, cambie su contraseña temporal antes de continuar.";

    /// <summary>Va después de UseAuthentication y antes de UseAuthorization.</summary>
    public static IApplicationBuilder UseCambioClaveObligatorio(this IApplicationBuilder app) =>
        app.Use(async (http, siguiente) =>
        {
            if (http.User.HasClaim(c => c.Type == Claim)
                && http.GetEndpoint()?.Metadata.GetMetadata<PermiteClaveTemporalAttribute>() is null)
            {
                throw new ErrorPermiso(Mensaje, Codigo);
            }

            await siguiente(http);
        });
}

/// <summary>Ruta que un usuario con clave temporal puede usar antes de cambiarla.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PermiteClaveTemporalAttribute : Attribute;
