namespace Sitbio.Domain.Instituciones;

/// <summary>
/// Institución cliente (tabla <c>tenant</c>), raíz del aislamiento. Por ahora solo se
/// lee para saber si su servicio está activo al iniciar sesión; HU-024 la completa.
/// </summary>
public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    public bool EstadoActivo { get; private set; }
}
