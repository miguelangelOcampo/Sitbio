using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Mantenimiento;

/// <summary>
/// Planes de mantenimiento preventivo (HU-019, RF-019; API-PLAN). Los permisos (crea y modifica AC-02;
/// consultan AC-01, AC-02, AC-03, AC-05 y AC-07) los exige la API. El próximo preventivo es el último +
/// la periodicidad, ajustable (la misma regla de CA-HU-002-06). El tenant sale del token.
/// </summary>
public sealed class GestionPlanes(
    IRepositorioPlanes repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "plan_preventivo";

    /// <summary>El equipo está dado de baja: no se le programan preventivos.</summary>
    public const string CodigoEquipoDeBaja = "PLN-001";

    /// <summary>El plan está finalizado y ya no cambia.</summary>
    public const string CodigoFinalizado = "PLN-002";

    public async Task<Pagina<PlanDto>> ListarAsync(FiltroPlanes filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<PlanDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<PlanDto>.TamanoMaximo}.");
        }

        var estado = string.IsNullOrWhiteSpace(filtro.Estado) ? null : filtro.Estado.Trim().ToLowerInvariant();
        if (estado is not null && !PlanPreventivo.Estados.Contains(estado))
        {
            throw ErrorValidacion.DeCampo("estado", "El estado debe ser activo, suspendido o finalizado.");
        }

        var (items, total) = await repositorio.ListarAsync(filtro with { Estado = estado }, cancellationToken);
        var hoy = FechaColombia.Hoy(tiempo);
        return new Pagina<PlanDto>(items.Select(p => PlanDto.Desde(p, hoy)).ToList(), filtro.Pagina, filtro.Tamano, total);
    }

    public async Task<PlanDto> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var leido = await repositorio.ObtenerAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("El plan preventivo");
        return PlanDto.Desde(leido, FechaColombia.Hoy(tiempo));
    }

    /// <summary>CA-HU-019-01: el plan con periodicidad y próxima fecha queda asociado al equipo, «activo».</summary>
    public async Task<PlanDto> CrearAsync(DatosPlan datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var hoy = FechaColombia.Hoy(tiempo);
        var errores = new Errores();
        if (datos.EquipoId is null || datos.EquipoId == Guid.Empty)
        {
            errores.Agregar("equipoId", "Selecciona el equipo del plan.");
        }

        var descripcion = Descripcion(datos.Descripcion, errores) ?? GestionEquipos.DescripcionPreventivo;
        var programa = Programa(datos.PeriodicidadDias, datos.UltimaEjecucion, datos.ProximaFecha, hoy, proximaAnterior: null, errores);
        errores.Lanzar();

        var equipo = await repositorio.EquipoAsync(datos.EquipoId!.Value, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        if (equipo.DadoDeBaja)
        {
            throw new ErrorConflicto(CodigoEquipoDeBaja, $"El equipo {equipo.PlacaInventario} está dado de baja; no se le programan preventivos.");
        }

        var (dias, ultima, proxima) = programa!.Value;
        var plan = new PlanPreventivo(contexto.TenantId, equipo.Id, descripcion, dias, proxima, tiempo.GetUtcNow(), ultima);
        repositorio.Agregar(plan);
        await repositorio.GuardarAsync(cancellationToken);
        await Auditar(AccionesAuditoria.Crear, plan, equipo.PlacaInventario, cancellationToken);
        return await ObtenerAsync(plan.Id, cancellationToken);
    }

    /// <summary>Cambia la descripción, el programa o el estado (suspender, reactivar o finalizar).</summary>
    public async Task<PlanDto> ActualizarAsync(Guid id, DatosActualizacionPlan datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var plan = await repositorio.PlanAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("El plan preventivo");
        if (plan.Estado == PlanPreventivo.Finalizado)
        {
            throw new ErrorConflicto(CodigoFinalizado, "El plan está finalizado; ya no se puede modificar.");
        }

        var hoy = FechaColombia.Hoy(tiempo);
        var errores = new Errores();
        var descripcion = datos.Descripcion is null ? null : Descripcion(datos.Descripcion, errores) ?? GestionEquipos.DescripcionPreventivo;
        var estado = string.IsNullOrWhiteSpace(datos.Estado) ? null : datos.Estado.Trim().ToLowerInvariant();
        if (estado is not null && !PlanPreventivo.Estados.Contains(estado))
        {
            errores.Agregar("estado", "El estado debe ser activo, suspendido o finalizado.");
        }

        var cambiaPrograma = datos.PeriodicidadDias is not null || datos.UltimaEjecucion is not null || datos.ProximaFecha is not null;
        var programa = cambiaPrograma
            ? Programa(
                datos.PeriodicidadDias ?? plan.PeriodicidadDias,
                datos.UltimaEjecucion ?? plan.UltimaEjecucion,
                datos.ProximaFecha ?? (datos.UltimaEjecucion is null && (datos.PeriodicidadDias is null || plan.UltimaEjecucion is null) ? plan.ProximaFecha : null),
                hoy,
                plan.ProximaFecha,
                errores)
            : null;
        errores.Lanzar();

        if (descripcion is not null)
        {
            plan.CambiarDescripcion(descripcion);
        }

        if (programa is { } nuevo)
        {
            plan.Programar(nuevo.Dias, nuevo.Proxima, nuevo.Ultima);
        }

        if (estado is not null)
        {
            plan.CambiarEstado(estado);
        }

        await repositorio.GuardarAsync(cancellationToken);
        var leido = await repositorio.ObtenerAsync(plan.Id, cancellationToken) ?? throw new ErrorNoEncontrado("El plan preventivo");
        await Auditar(AccionesAuditoria.Actualizar, plan, leido.PlacaInventario, cancellationToken);
        return PlanDto.Desde(leido, hoy);
    }

    private static string? Descripcion(string? texto, Errores errores)
    {
        var descripcion = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        if (descripcion is { Length: > PlanPreventivo.LongitudMaximaDescripcion })
        {
            errores.Agregar("descripcion", $"Máximo {PlanPreventivo.LongitudMaximaDescripcion} caracteres.");
        }

        return descripcion;
    }

    /// <summary>
    /// Periodicidad, último y próximo: el próximo es último + periodicidad si no se indica; sin último, el
    /// próximo no puede haber pasado (salvo que no cambie); el último no es futuro y es anterior al próximo.
    /// </summary>
    private static (int Dias, DateOnly? Ultima, DateOnly Proxima)? Programa(
        int? periodicidad,
        DateOnly? ultima,
        DateOnly? proximaIndicada,
        DateOnly hoy,
        DateOnly? proximaAnterior,
        Errores errores)
    {
        var valido = true;
        if (periodicidad is not (>= 1 and <= short.MaxValue))
        {
            errores.Agregar("periodicidadDias", "La periodicidad del preventivo debe ser de al menos un día.");
            valido = false;
        }

        if (ultima is { } u && u > hoy)
        {
            errores.Agregar("ultimaEjecucion", "El último preventivo no puede ser una fecha futura.");
            valido = false;
        }

        if (!valido)
        {
            return null;
        }

        var proxima = proximaIndicada ?? ultima?.AddDays(periodicidad!.Value);
        if (proxima is null)
        {
            errores.Agregar("proximaFecha", "Si no se conoce el último preventivo, indique la fecha del próximo.");
            return null;
        }

        if (ultima is null && proxima < hoy && proxima != proximaAnterior)
        {
            errores.Agregar("proximaFecha", "El próximo preventivo no puede ser una fecha pasada.");
            return null;
        }

        if (ultima is { } anterior && proxima <= anterior)
        {
            errores.Agregar("proximaFecha", "El próximo preventivo debe ser posterior al último.");
            return null;
        }

        return (periodicidad!.Value, ultima, proxima.Value);
    }

    private Task Auditar(string accion, PlanPreventivo plan, string placa, CancellationToken cancellationToken) =>
        auditoria.RegistrarAsync(
            contexto.UsuarioId,
            accion,
            Entidad,
            plan.Id.ToString(),
            new Dictionary<string, string>
            {
                ["equipo"] = placa,
                ["periodicidadDias"] = plan.PeriodicidadDias.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["proximaFecha"] = plan.ProximaFecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["estado"] = plan.Estado,
            },
            cancellationToken);

    private sealed class Errores
    {
        private readonly Dictionary<string, string[]> _errores = [];

        public void Agregar(string campo, string mensaje) => _errores[campo] = [mensaje];

        public void Lanzar()
        {
            if (_errores.Count > 0)
            {
                throw new ErrorValidacion(_errores);
            }
        }
    }
}
