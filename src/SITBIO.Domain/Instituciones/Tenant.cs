using System.Diagnostics.CodeAnalysis;

namespace Sitbio.Domain.Instituciones;

/// <summary>
/// Institución cliente (tabla <c>tenant</c>), raíz del aislamiento. Por ahora solo se
/// lee para saber si su servicio está activo al iniciar sesión; HU-024 la completa.
/// </summary>
[SuppressMessage("Major Bug", "S3453:Classes should not have only private constructors",
    Justification = "EF Core materializa la entidad con el constructor privado; los tenants los crea el aprovisionamiento (ST-10).")]
public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    public bool EstadoActivo { get; private set; }
}
