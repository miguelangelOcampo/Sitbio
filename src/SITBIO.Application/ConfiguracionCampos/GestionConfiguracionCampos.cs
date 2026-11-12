using Sitbio.Application.Auditoria;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.ConfiguracionCampos;

/// <summary>Una versión guardada en <c>configuracion_campos</c> (MongoDB) para un tipo de equipo de la institución.</summary>
public sealed record VersionConfiguracion(
    Guid TipoEquipoId,
    int Version,
    string Estado,
    Guid CreadoPor,
    DateTimeOffset FechaCreacion,
    IReadOnlyList<CampoDinamico> Campos);

/// <summary>Resumen de una versión para el historial de UI-018.</summary>
public sealed record ResumenVersion(int Version, string Estado, DateTimeOffset FechaCreacion, int Campos);

/// <summary>
/// Respuesta de API-CFG: la versión activa del tipo de equipo con sus campos y el historial de
/// versiones. Si el tipo aún no tiene campos, <see cref="Version"/> va nula y la lista vacía.
/// </summary>
public sealed record ConfiguracionCamposDto(
    Guid TipoEquipoId,
    string TipoEquipo,
    int? Version,
    DateTimeOffset? FechaPublicacion,
    IReadOnlyList<CampoDinamico> Campos,
    IReadOnlyList<ResumenVersion> Versiones);

/// <summary>Cuerpo de POST: la lista completa de campos de la nueva versión.</summary>
public sealed record DatosConfiguracionCampos(IReadOnlyList<CampoDinamico?>? Campos);

/// <summary>Códigos de error estables de API-CFG (409).</summary>
public static class ErroresConfiguracionCampos
{
    /// <summary>Otra persona publicó una versión del mismo tipo al mismo tiempo.</summary>
    public const string CodigoVersionConcurrente = "CFG-001";
}

/// <summary>
/// Versiones de <c>configuracion_campos</c> de la institución del token (RN-008): el repositorio
/// filtra y asigna el tenant. No se borran ni se editan versiones: publicar crea una nueva.
/// </summary>
public interface IRepositorioConfiguracionCampos
{
    /// <summary>Versiones del tipo de equipo, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<VersionConfiguracion>> VersionesAsync(Guid tipoEquipoId, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda <paramref name="nueva"/> como activa y deja las anteriores como históricas. Si el
    /// número de versión ya existe (publicación simultánea) responde 409 <see cref="ErroresConfiguracionCampos.CodigoVersionConcurrente"/>.
    /// </summary>
    Task PublicarAsync(VersionConfiguracion nueva, CancellationToken cancellationToken);
}

/// <summary>
/// Motor de campos dinámicos (HU-006, RF-006, API-CFG). El Ingeniero Biomédico (AC-02) define y
/// publica versiones de los campos por tipo de equipo; el Administrador (AC-01) y el Técnico (AC-03)
/// solo consultan (CA-HU-006-03). Cada institución ve solo sus campos (CA-HU-006-02). Las
/// intervenciones ya registradas conservan la versión con la que se guardaron.
/// </summary>
public sealed class GestionConfiguracionCampos(
    IRepositorioConfiguracionCampos repositorio,
    IRepositorioCatalogos catalogos,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "configuracion_campos";

    public async Task<ConfiguracionCamposDto> ObtenerAsync(Guid tipoEquipoId, CancellationToken cancellationToken)
    {
        var tipo = await TipoEquipoAsync(tipoEquipoId, cancellationToken);
        var versiones = await repositorio.VersionesAsync(tipoEquipoId, cancellationToken);
        return Dto(tipoEquipoId, tipo, versiones);
    }

    public async Task<ConfiguracionCamposDto> PublicarAsync(Guid tipoEquipoId, DatosConfiguracionCampos datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (contexto.Rol != CodigosRol.IngenieroBiomedico)
        {
            throw new ErrorPermiso("Solo el Ingeniero Biomédico define y publica los campos dinámicos; puedes consultarlos.");
        }

        var tipo = await TipoEquipoAsync(tipoEquipoId, cancellationToken);
        var (campos, errores) = ReglasCamposDinamicos.Normalizar(datos.Campos);
        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var anteriores = await repositorio.VersionesAsync(tipoEquipoId, cancellationToken);
        var activa = anteriores.FirstOrDefault(v => v.Estado == EstadosConfiguracion.Activa) ?? anteriores.FirstOrDefault();
        if (activa is not null && activa.Campos.SequenceEqual(campos))
        {
            throw ErrorValidacion.DeCampo("campos", $"La versión {activa.Version} ya tiene estos campos; cambia algo antes de publicar.");
        }

        var nueva = new VersionConfiguracion(
            tipoEquipoId,
            (anteriores.Count == 0 ? 0 : anteriores.Max(v => v.Version)) + 1,
            EstadosConfiguracion.Activa,
            contexto.UsuarioId,
            tiempo.GetUtcNow(),
            campos);
        await repositorio.PublicarAsync(nueva, cancellationToken);

        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Crear,
            Entidad,
            tipoEquipoId.ToString(),
            new Dictionary<string, string>
            {
                ["tipo_equipo"] = tipo,
                ["version"] = nueva.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["campos"] = string.Join(", ", campos.Select(c => c.Nombre)),
            },
            cancellationToken);

        return Dto(tipoEquipoId, tipo, [nueva, .. anteriores.Select(v => v with { Estado = EstadosConfiguracion.Historica })]);
    }

    /// <summary>Nombre del tipo de equipo de la institución; 404 si no existe o es de otra (SEC-003).</summary>
    private async Task<string> TipoEquipoAsync(Guid tipoEquipoId, CancellationToken cancellationToken) =>
        (await catalogos.ObtenerAsync(TiposCatalogo.TiposEquipo, tipoEquipoId, cancellationToken))?.Nombre
        ?? throw new ErrorNoEncontrado("tipo de equipo");

    private static ConfiguracionCamposDto Dto(Guid tipoEquipoId, string tipo, IReadOnlyList<VersionConfiguracion> versiones)
    {
        var activa = versiones.FirstOrDefault(v => v.Estado == EstadosConfiguracion.Activa) ?? versiones.FirstOrDefault();
        return new ConfiguracionCamposDto(
            tipoEquipoId,
            tipo,
            activa?.Version,
            activa?.FechaCreacion,
            activa?.Campos ?? [],
            versiones.Select(v => new ResumenVersion(v.Version, v.Estado, v.FechaCreacion, v.Campos.Count)).ToList());
    }
}
