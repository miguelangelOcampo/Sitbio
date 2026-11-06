using System.Diagnostics.CodeAnalysis;
using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Ubicación (área o servicio) de la institución (tabla <c>ubicacion</c>, script 01). La gestión
/// del catálogo llega con HU-025; aquí se mapea para validar la ubicación de un equipo.
/// </summary>
[SuppressMessage("Major Bug", "S3453:Classes should not have only private constructors",
    Justification = "EF Core la materializa; el catálogo se gestiona con HU-025.")]
public sealed class Ubicacion : EntidadTenant
{
    private Ubicacion(Guid tenantId)
        : base(tenantId)
    {
    }

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public bool EstadoActivo { get; private set; }
}

/// <summary>Proveedor de la institución (tabla <c>proveedor</c>, script 01). Gestión con HU-025.</summary>
[SuppressMessage("Major Bug", "S3453:Classes should not have only private constructors",
    Justification = "EF Core lo materializa; el catálogo se gestiona con HU-025.")]
public sealed class Proveedor : EntidadTenant
{
    private Proveedor(Guid tenantId)
        : base(tenantId)
    {
    }

    public string Nombre { get; private set; } = string.Empty;

    public string Nit { get; private set; } = string.Empty;

    public string TipoProveedor { get; private set; } = string.Empty;

    public bool EstadoActivo { get; private set; }
}
