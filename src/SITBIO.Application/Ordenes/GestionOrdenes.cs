using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Ordenes;

/// <summary>
/// Asignación y consulta de órdenes de trabajo (HU-004; RF-007; RN-003). Los permisos (asigna AC-02;
/// consultan AC-01, AC-02, AC-03 y AC-07) los exige la API. El Técnico de Mantenimiento solo ve las
/// órdenes que tiene asignadas (UI-012). El tenant y quién asigna salen del token.
/// </summary>
public sealed class GestionOrdenes(
    IRepositorioOrdenes repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "orden_trabajo";

    /// <summary>El tiquete ya tiene orden de trabajo (no está «recibido»).</summary>
    public const string CodigoTiqueteAsignado = "OT-001";

    public const string MensajeRn003 = "Solo puedes asignar la orden a un Técnico de Mantenimiento activo de tu institución (RN-003).";

    /// <summary>
    /// CA-HU-004-01: crea la orden del tiquete «recibido» para un técnico activo; el tiquete pasa a
    /// «asignado» y la orden queda en el panel del técnico. CA-HU-004-02: un usuario inactivo o sin rol
    /// de Técnico se rechaza (RN-003).
    /// </summary>
    public async Task<OrdenDto> AsignarAsync(DatosOrden datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var (tiqueteId, tecnicoId, prioridad) = Validar(datos);

        var tiquete = await repositorio.TiqueteAsync(tiqueteId, cancellationToken) ?? throw new ErrorNoEncontrado("El tiquete");
        if (tiquete.Estado != EstadosTiquete.Recibido)
        {
            throw new ErrorConflicto(CodigoTiqueteAsignado, $"El tiquete {tiquete.Codigo} ya tiene una orden de trabajo.");
        }

        var tecnico = await repositorio.TecnicoAsync(tecnicoId, cancellationToken);
        if (tecnico is not { CodigoRol: CodigosRol.TecnicoMantenimiento, Activo: true })
        {
            throw ErrorValidacion.DeCampo("tecnicoId", MensajeRn003);
        }

        var (orden, cambio) = OrdenTrabajo.Correctiva(tiquete, tecnico.Id, contexto.UsuarioId, prioridad, datos.FechaProgramada, tiempo.GetUtcNow());
        await repositorio.RegistrarAsync(orden, cambio, cancellationToken);
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Crear,
            Entidad,
            orden.Id.ToString(),
            new Dictionary<string, string>
            {
                ["tiquete"] = tiquete.Codigo,
                ["prioridad"] = orden.Prioridad,
                ["tipo"] = orden.TipoMantenimiento,
            },
            cancellationToken);

        var leida = await repositorio.ObtenerAsync(orden.Id, null, cancellationToken)
            ?? throw new InvalidOperationException("La orden recién guardada no se pudo leer.");
        return OrdenDto.Desde(leida);
    }

    public async Task<Pagina<OrdenDto>> ListarAsync(FiltroOrdenes filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<OrdenDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<OrdenDto>.TamanoMaximo}.");
        }

        var estado = string.IsNullOrWhiteSpace(filtro.Estado) ? null : filtro.Estado.Trim().ToLowerInvariant();
        if (estado is not null && !EstadosOrden.Todos.Contains(estado))
        {
            throw ErrorValidacion.DeCampo("estado", $"El estado debe ser {string.Join(", ", EstadosOrden.Todos)}.");
        }

        var tecnico = SoloAsignadas() ?? (filtro.AsignadasAMi ? contexto.UsuarioId : null);
        var (items, total) = await repositorio.ListarAsync(filtro with { Estado = estado }, tecnico, cancellationToken);
        return new Pagina<OrdenDto>(items.Select(OrdenDto.Desde).ToList(), filtro.Pagina, filtro.Tamano, total);
    }

    /// <summary>Orden por id; 404 si es de otra institución o, para el técnico, de otro técnico.</summary>
    public async Task<OrdenDto> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var leida = await repositorio.ObtenerAsync(id, SoloAsignadas(), cancellationToken) ?? throw new ErrorNoEncontrado("La orden de trabajo");
        return OrdenDto.Desde(leida);
    }

    private (Guid TiqueteId, Guid TecnicoId, string? Prioridad) Validar(DatosOrden datos)
    {
        var errores = new Dictionary<string, string[]>();
        if (datos.TiqueteId is null || datos.TiqueteId == Guid.Empty)
        {
            errores["tiqueteId"] = ["Selecciona el tiquete que vas a asignar."];
        }

        if (datos.TecnicoId is null || datos.TecnicoId == Guid.Empty)
        {
            errores["tecnicoId"] = ["Selecciona el técnico que ejecutará la orden."];
        }

        var prioridad = string.IsNullOrWhiteSpace(datos.Prioridad) ? null : datos.Prioridad.Trim().ToLowerInvariant();
        if (prioridad is not null && !Criticidades.EsValida(prioridad))
        {
            errores["prioridad"] = ["La prioridad debe ser crítica, alta, media o baja."];
        }

        if (datos.FechaProgramada is { } fecha && fecha < FechaColombia.Hoy(tiempo))
        {
            errores["fechaProgramada"] = ["La fecha programada no puede ser anterior a hoy."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        return (datos.TiqueteId!.Value, datos.TecnicoId!.Value, prioridad);
    }

    private Guid? SoloAsignadas() => contexto.Rol == CodigosRol.TecnicoMantenimiento ? contexto.UsuarioId : null;
}
