namespace Sitbio.Domain.Seguridad;

/// <summary>
/// Rol del modelo RBAC (tabla <c>rol</c>). Es global: no pertenece a un tenant.
/// Los roles los crea el esquema (script 01 y V004); la aplicación los identifica
/// por <see cref="Codigo"/> (ver <see cref="CodigosRol"/>), nunca por el id.
/// </summary>
public sealed class Rol
{
    private Rol()
    {
    }

    public int Id { get; private set; }

    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;
}
