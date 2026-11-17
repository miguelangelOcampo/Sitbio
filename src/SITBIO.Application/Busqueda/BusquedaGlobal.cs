using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Busqueda;

/// <summary>Respuesta de GET /busqueda: hasta <see cref="BusquedaGlobal.MaximoPorGrupo"/> resultados por grupo.</summary>
public sealed record ResultadoBusquedaDto(
    IReadOnlyList<EquipoEncontradoDto> Equipos,
    IReadOnlyList<TiqueteEncontradoDto> Tiquetes,
    IReadOnlyList<OrdenEncontradaDto> Ordenes)
{
    public static ResultadoBusquedaDto Vacio { get; } = new([], [], []);
}

/// <summary>Equipo encontrado por su código de activo (placa) o su número de serie; abre la hoja de vida (UI-006).</summary>
public sealed record EquipoEncontradoDto(Guid Id, string PlacaInventario, string NumeroSerie, string TipoEquipo, string Ubicacion);

/// <summary>Tiquete encontrado por su código TQT-######; abre el seguimiento (UI-010).</summary>
public sealed record TiqueteEncontradoDto(string Codigo, string TipoFalla, string Estado, string PlacaInventario);

/// <summary>
/// Orden encontrada por su referencia: el código del tiquete de origen (correctiva) o, en una preventiva, el código
/// de activo del equipo, que es como la nombran UI-012 a UI-014.
/// </summary>
public sealed record OrdenEncontradaDto(Guid Id, string Referencia, string TipoMantenimiento, string Estado, string PlacaInventario);

public interface IRepositorioBusqueda
{
    /// <summary>Equipos cuya placa o serie contiene <paramref name="texto"/> (sin distinguir mayúsculas).</summary>
    Task<IReadOnlyList<EquipoEncontradoDto>> EquiposAsync(string texto, int maximo, CancellationToken cancellationToken);

    /// <summary>Tiquetes cuyo código contiene el texto, del más reciente al más antiguo; con reportante, solo los suyos.</summary>
    Task<IReadOnlyList<TiqueteEncontradoDto>> TiquetesAsync(string texto, Guid? reportanteId, int maximo, CancellationToken cancellationToken);

    /// <summary>Órdenes cuya referencia contiene el texto, de la más reciente a la más antigua; con técnico, solo las asignadas a él.</summary>
    Task<IReadOnlyList<OrdenEncontradaDto>> OrdenesAsync(string texto, Guid? tecnicoId, int maximo, CancellationToken cancellationToken);
}

/// <summary>
/// Búsqueda global de la barra superior (D-04; prototipo App.tsx, busqueda-global): equipos por placa o serie,
/// tiquetes por código y órdenes por referencia, sobre la institución del token (RN-008). Cada grupo respeta la
/// matriz de permisos del DOC-03: el Personal Clínico (AC-04) solo ve los tiquetes que reportó (CA-HU-017-02) y el
/// Técnico (AC-03) solo las órdenes asignadas a él; un rol sin consulta de un módulo recibe ese grupo vacío.
/// </summary>
public sealed class BusquedaGlobal(IRepositorioBusqueda repositorio, IContextoUsuario contexto)
{
    public const int MaximoPorGrupo = 5;
    public const int LongitudMinima = 2;
    public const int LongitudMaxima = 60;

    /// <summary>Roles que consultan el inventario (API-EQ).</summary>
    public static IReadOnlyList<string> RolesEquipos { get; } =
    [
        CodigosRol.AdministradorSistema,
        CodigosRol.IngenieroBiomedico,
        CodigosRol.TecnicoMantenimiento,
        CodigosRol.PersonalClinico,
        CodigosRol.GerenteBiomedico,
        CodigosRol.EnteRegulador,
    ];

    /// <summary>Roles que consultan tiquetes (API-TIQ, HU-017).</summary>
    public static IReadOnlyList<string> RolesTiquetes { get; } =
    [
        CodigosRol.AdministradorSistema,
        CodigosRol.IngenieroBiomedico,
        CodigosRol.TecnicoMantenimiento,
        CodigosRol.PersonalClinico,
        CodigosRol.EnteRegulador,
    ];

    /// <summary>Roles que consultan órdenes de trabajo (API-OT).</summary>
    public static IReadOnlyList<string> RolesOrdenes { get; } =
    [
        CodigosRol.AdministradorSistema,
        CodigosRol.IngenieroBiomedico,
        CodigosRol.TecnicoMantenimiento,
        CodigosRol.EnteRegulador,
    ];

    /// <summary>Roles con algún grupo: la Directiva (AC-06) solo consulta el tablero y no usa la búsqueda.</summary>
    public static IReadOnlyList<string> Roles { get; } = RolesEquipos;

    /// <summary>Con menos de <see cref="LongitudMinima"/> caracteres no busca y devuelve los grupos vacíos.</summary>
    public async Task<ResultadoBusquedaDto> BuscarAsync(string? q, CancellationToken cancellationToken)
    {
        var texto = q?.Trim() ?? string.Empty;
        if (texto.Length > LongitudMaxima)
        {
            throw ErrorValidacion.DeCampo("q", $"La búsqueda admite hasta {LongitudMaxima} caracteres.");
        }

        if (texto.Length < LongitudMinima)
        {
            return ResultadoBusquedaDto.Vacio;
        }

        var rol = contexto.Rol;
        IReadOnlyList<EquipoEncontradoDto> equipos = RolesEquipos.Contains(rol)
            ? await repositorio.EquiposAsync(texto, MaximoPorGrupo, cancellationToken)
            : [];
        IReadOnlyList<TiqueteEncontradoDto> tiquetes = RolesTiquetes.Contains(rol)
            ? await repositorio.TiquetesAsync(texto, rol == CodigosRol.PersonalClinico ? contexto.UsuarioId : null, MaximoPorGrupo, cancellationToken)
            : [];
        IReadOnlyList<OrdenEncontradaDto> ordenes = RolesOrdenes.Contains(rol)
            ? await repositorio.OrdenesAsync(texto, rol == CodigosRol.TecnicoMantenimiento ? contexto.UsuarioId : null, MaximoPorGrupo, cancellationToken)
            : [];
        return new ResultadoBusquedaDto(equipos, tiquetes, ordenes);
    }
}
