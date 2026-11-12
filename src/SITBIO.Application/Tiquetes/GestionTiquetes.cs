using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tiquetes;

/// <summary>
/// Reporte y consulta de tiquetes (HU-003, HU-018; RF-004, RF-018; RN-005). Los permisos (reportan
/// AC-02 y AC-04; consultan AC-02, AC-03 y AC-04) los exige la API. El Personal Clínico solo ve los
/// tiquetes que reportó (DOC-06, Tabla 37). El tenant y quien reporta salen del token.
/// </summary>
public sealed class GestionTiquetes(
    IRepositorioTiquetes repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "tiquete";
    public const string CodigoFueraDeServicio = "FUERA_DE_SERVICIO";
    public const string MotivoFallaCritica = "Falla crítica reportada (RN-005)";

    /// <summary>
    /// CA-HU-003-01: genera el código único. CA-HU-018-02 (RN-005): con criticidad «crítica», el equipo
    /// pasa a «Fuera de servicio» en la misma transacción y queda en su historial de estados.
    /// </summary>
    public async Task<TiqueteDto> ReportarAsync(DatosTiquete datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var (equipoId, tipoFalla, descripcion, criticidad) = await Validar(datos, cancellationToken);

        var equipo = await repositorio.EquipoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        if (equipo.DadoDeBaja)
        {
            throw ErrorValidacion.DeCampo("equipoId", "El equipo está dado de baja; no admite reportes de falla.");
        }

        var ahora = tiempo.GetUtcNow();
        var tiquete = new Tiquete(contexto.TenantId, equipo.Id, contexto.UsuarioId, tipoFalla, descripcion, criticidad, ahora);
        var cambioEquipo = tiquete.EsCritica && equipo.EstadoEquipoId != EstadosEquipo.FueraDeServicio
            ? equipo.CambiarEstado(EstadosEquipo.FueraDeServicio, $"{MotivoFallaCritica}: {tipoFalla}", contexto.UsuarioId, ahora)
            : null;

        await repositorio.RegistrarAsync(tiquete, tiquete.Recepcion(), cambioEquipo, cancellationToken);
        await Auditar(tiquete, equipo, cambioEquipo is not null, cancellationToken);

        var leido = await repositorio.ObtenerAsync(tiquete.Codigo, null, cancellationToken)
            ?? throw new InvalidOperationException("El tiquete recién guardado no se pudo leer.");
        return TiqueteDto.Desde(leido);
    }

    public async Task<Pagina<TiqueteDto>> ListarAsync(FiltroTiquetes filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<TiqueteDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<TiqueteDto>.TamanoMaximo}.");
        }

        var estado = Limpio(filtro.Estado)?.ToLowerInvariant();
        if (estado is not null && !EstadosTiquete.Todos.Contains(estado))
        {
            throw ErrorValidacion.DeCampo("estado", $"El estado debe ser {string.Join(", ", EstadosTiquete.Todos)}.");
        }

        var criticidad = Limpio(filtro.Criticidad)?.ToLowerInvariant();
        if (criticidad is not null && !Criticidades.EsValida(criticidad))
        {
            throw ErrorValidacion.DeCampo("criticidad", $"La criticidad debe ser {string.Join(", ", Criticidades.Todas)}.");
        }

        var (items, total) = await repositorio.ListarAsync(filtro with { Estado = estado, Criticidad = criticidad }, SoloPropios(), cancellationToken);
        return new Pagina<TiqueteDto>(items.Select(TiqueteDto.Desde).ToList(), filtro.Pagina, filtro.Tamano, total);
    }

    /// <summary>Tiquete por su código (TQT-######); 404 si es de otra institución o, para AC-04, de otra persona.</summary>
    public async Task<TiqueteDto> ObtenerAsync(string codigo, CancellationToken cancellationToken)
    {
        var normalizado = Limpio(codigo)?.ToUpperInvariant();
        if (Tiquete.Consecutivo(normalizado) is null)
        {
            throw new ErrorNoEncontrado("El tiquete");
        }

        var leido = await repositorio.ObtenerAsync(normalizado!, SoloPropios(), cancellationToken) ?? throw new ErrorNoEncontrado("El tiquete");
        return TiqueteDto.Desde(leido);
    }

    /// <summary>CA-HU-003-02: sin equipo no hay tiquete; todos los errores llegan juntos y en español.</summary>
    private async Task<(Guid EquipoId, string TipoFalla, string Descripcion, string Criticidad)> Validar(DatosTiquete datos, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, string[]>();
        if (datos.EquipoId is null || datos.EquipoId == Guid.Empty)
        {
            errores["equipoId"] = ["Selecciona el equipo que presenta la falla."];
        }

        var tipoFalla = Limpio(datos.TipoFalla);
        if (tipoFalla is null)
        {
            errores["tipoFalla"] = ["Elige el tipo de falla."];
        }
        else
        {
            tipoFalla = await repositorio.TipoFallaActivoAsync(tipoFalla, cancellationToken);
            if (tipoFalla is null)
            {
                errores["tipoFalla"] = ["Ese tipo de falla no está activo en el catálogo de tu institución."];
            }
        }

        var descripcion = Limpio(datos.Descripcion);
        if (descripcion is null)
        {
            errores["descripcion"] = ["Describe la falla."];
        }
        else if (descripcion.Length > Tiquete.LongitudMaximaDescripcion)
        {
            errores["descripcion"] = [$"La descripción admite máximo {Tiquete.LongitudMaximaDescripcion} caracteres."];
        }

        var criticidad = Limpio(datos.Criticidad)?.ToLowerInvariant();
        if (!Criticidades.EsValida(criticidad))
        {
            errores["criticidad"] = ["Elige la criticidad: crítica, alta, media o baja."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        return (datos.EquipoId!.Value, tipoFalla!, descripcion!, criticidad!);
    }

    private Guid? SoloPropios() => contexto.Rol == CodigosRol.PersonalClinico ? contexto.UsuarioId : null;

    /// <summary>Bitácora: el tiquete creado y, con RN-005, el cambio de estado del equipo. Sin datos personales.</summary>
    private async Task Auditar(Tiquete tiquete, EquipoBiomedico equipo, bool equipoFueraDeServicio, CancellationToken cancellationToken)
    {
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Crear,
            Entidad,
            tiquete.Id.ToString(),
            new Dictionary<string, string>
            {
                ["codigo"] = tiquete.Codigo,
                ["criticidad"] = tiquete.Criticidad,
                ["placa"] = equipo.PlacaInventario,
            },
            cancellationToken);

        if (equipoFueraDeServicio)
        {
            await auditoria.RegistrarAsync(
                contexto.UsuarioId,
                AccionesAuditoria.Actualizar,
                GestionEquipos.Entidad,
                equipo.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["placa"] = equipo.PlacaInventario,
                    ["estado"] = GestionTiquetes.CodigoFueraDeServicio,
                    ["tiquete"] = tiquete.Codigo,
                    ["regla"] = "RN-005",
                },
                cancellationToken);
        }
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
