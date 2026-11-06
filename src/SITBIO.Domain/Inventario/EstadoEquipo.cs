using System.Diagnostics.CodeAnalysis;

namespace Sitbio.Domain.Inventario;

/// <summary>Catálogo global de estados del equipo (tabla <c>estado_equipo</c>, script 01). Sin tenant.</summary>
[SuppressMessage("Major Bug", "S3453:Classes should not have only private constructors",
    Justification = "EF Core materializa la entidad con el constructor privado; los estados los crea el script 01.")]
public sealed class EstadoEquipo
{
    private EstadoEquipo()
    {
    }

    public short Id { get; private set; }

    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }
}

/// <summary>Ids fijos de <c>estado_equipo</c> (script 01).</summary>
public static class EstadosEquipo
{
    public const short Operativo = 1;
    public const short EnMantenimiento = 2;
    public const short FueraDeServicio = 3;
    public const short Baja = 4;
}
