using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.ConfiguracionCampos;
using Sitbio.Domain.Configuracion;
using Sitbio.Domain.Mantenimiento;
using Sitbio.Domain.Peticiones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Ordenes;

/// <summary>
/// Ejecución de la orden por el técnico asignado (HU-005; RF-005, RF-006; RT-002). El técnico inicia y
/// pausa su orden y registra la hoja de servicio en <c>intervenciones</c> (MongoDB), validando los
/// <c>camposDinamicos</c> contra la versión activa del tipo de equipo. Solo el técnico asignado actúa:
/// para cualquier otro la orden «no existe» (404, igual que en la consulta). Primero se guarda en
/// MongoDB y luego el enlace en PostgreSQL: si MongoDB falla, la orden no cambia (RT-002).
/// </summary>
public sealed class GestionEjecucion(
    IRepositorioOrdenes ordenes,
    IRepositorioIntervenciones intervenciones,
    IRepositorioConfiguracionCampos configuracion,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string EntidadIntervencion = "intervenciones";

    /// <summary>La acción no aplica al estado actual de la orden.</summary>
    public const string CodigoTransicionInvalida = "OT-002";

    /// <summary>La orden ya se cerró y espera la aprobación del Ingeniero (HU-021).</summary>
    public const string CodigoOrdenCerrada = "OT-003";

    /// <summary>La intervención está firmada o la orden aprobada: ya no se modifica (RN-004).</summary>
    public const string CodigoFirmada = "RN-004";

    public const int LongitudMaximaTexto = 4000;
    public const int MaximoRepuestos = 50;
    public const int LongitudMaximaRepuesto = 150;
    public const int CantidadMaxima = 10_000;
    public const int TiempoMaximoMin = 100_000;

    private const string MensajeFirmada = "La intervención ya está firmada y aprobada; no se puede modificar (RN-004).";

    /// <summary>
    /// API-OT-ESTADO: «iniciar» (asignada o pausada → en ejecución) o «pausar» (en ejecución → pausada).
    /// La primera vez que se inicia, el tiquete de origen pasa de «asignado» a «en ejecución».
    /// </summary>
    public async Task<OrdenDto> CambiarEstadoAsync(Guid id, DatosEstadoOrden datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var accion = datos.Accion?.Trim().ToLowerInvariant();
        if (accion is null || !AccionesOrden.Todas.Contains(accion))
        {
            throw ErrorValidacion.DeCampo("accion", $"La acción debe ser {string.Join(" o ", AccionesOrden.Todas)}.");
        }

        var orden = await PropiaAsync(id, cancellationToken);
        var anterior = orden.Estado;
        HistorialTiquete? cambio = null;
        try
        {
            if (accion == AccionesOrden.Pausar)
            {
                orden.Pausar();
            }
            else if (orden.Iniciar(tiempo.GetUtcNow()) && orden.TiqueteId is { } tiqueteId)
            {
                var tiquete = await ordenes.TiqueteAsync(tiqueteId, cancellationToken);
                if (tiquete?.Estado == EstadosTiquete.Asignado)
                {
                    cambio = tiquete.IniciarEjecucion(contexto.UsuarioId, "El técnico inició la orden de trabajo.", tiempo.GetUtcNow());
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw new ErrorConflicto(CodigoTransicionInvalida, MensajeTransicion(accion, anterior));
        }

        await ordenes.GuardarAsync(cambio, cancellationToken);
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Actualizar,
            GestionOrdenes.Entidad,
            orden.Id.ToString(),
            new Dictionary<string, string> { ["accion"] = accion, ["estado_anterior"] = anterior, ["estado"] = orden.Estado },
            cancellationToken);

        var leida = await ordenes.ObtenerAsync(orden.Id, null, cancellationToken)
            ?? throw new InvalidOperationException("La orden recién guardada no se pudo leer.");
        return OrdenDto.Desde(leida);
    }

    /// <summary>
    /// API-INT (PUT): registra o corrige la hoja de servicio de la orden en ejecución o pausada. Valida
    /// los campos fijos y los dinámicos (versión activa del tipo de equipo) y calcula el costo total.
    /// </summary>
    public async Task<IntervencionDto> RegistrarIntervencionAsync(Guid ordenId, DatosIntervencion datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var orden = await PropiaAsync(ordenId, cancellationToken);
        switch (orden.Estado)
        {
            case EstadosOrden.Aprobada:
                throw new ErrorConflicto(CodigoFirmada, MensajeFirmada);
            case EstadosOrden.Cerrada:
                throw new ErrorConflicto(CodigoOrdenCerrada, "La orden ya se cerró y espera la aprobación del Ingeniero; no se puede modificar la intervención.");
            case EstadosOrden.Asignada:
                throw new ErrorConflicto(CodigoTransicionInvalida, "Inicia la orden antes de registrar la intervención.");
        }

        var existente = await intervenciones.ObtenerAsync(orden.Id, cancellationToken);
        if (existente?.Intervencion.CerradaFirmada == true)
        {
            throw new ErrorConflicto(CodigoFirmada, MensajeFirmada);
        }

        var tipoEquipo = await ordenes.TipoEquipoAsync(orden.EquipoId, cancellationToken)
            ?? throw new InvalidOperationException("El equipo de la orden no se encontró.");
        var versiones = await configuracion.VersionesAsync(tipoEquipo, cancellationToken);
        var vigente = versiones.FirstOrDefault(v => v.Estado == EstadosConfiguracion.Activa) ?? versiones.FirstOrDefault();

        var (dinamicos, errores) = ValoresCamposDinamicos.Validar(vigente?.Campos ?? [], datos.CamposDinamicos);
        var (diagnostico, acciones, repuestos, salida) = Validar(datos, errores);
        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var costoRepuestos = repuestos.Sum(r => r.Cantidad * (r.CostoUnitario ?? 0m));
        var intervencion = new Intervencion(
            existente?.Intervencion.Id ?? Guid.NewGuid(),
            orden.Id,
            orden.EquipoId,
            orden.TecnicoId,
            orden.TipoMantenimiento,
            diagnostico,
            acciones,
            repuestos,
            datos.CostoManoObra,
            costoRepuestos + (datos.CostoManoObra ?? 0m),
            datos.TiempoAtencionMin,
            salida,
            dinamicos,
            vigente?.Version,
            existente?.Intervencion.FechaIntervencion ?? tiempo.GetUtcNow(),
            CerradaFirmada: false);

        // RT-002: MongoDB primero. Si falla, la excepción sale antes de tocar la orden en PostgreSQL.
        var objectId = await intervenciones.GuardarAsync(intervencion, existente?.ObjectId, cancellationToken);
        if (orden.IntervencionId != objectId)
        {
            orden.EnlazarIntervencion(objectId);
            await ordenes.GuardarAsync(null, cancellationToken);
        }

        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            existente is null ? AccionesAuditoria.Crear : AccionesAuditoria.Actualizar,
            EntidadIntervencion,
            objectId,
            new Dictionary<string, string>
            {
                ["orden"] = orden.Id.ToString(),
                ["version_campos"] = vigente?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "ninguna",
            },
            cancellationToken);

        return IntervencionDto.Desde(intervencion);
    }

    /// <summary>API-INT (GET): la intervención de la orden; para el técnico, solo de sus órdenes. 404 si aún no hay.</summary>
    public async Task<IntervencionDto> ObtenerIntervencionAsync(Guid ordenId, CancellationToken cancellationToken)
    {
        var tecnico = contexto.Rol == CodigosRol.TecnicoMantenimiento ? contexto.UsuarioId : (Guid?)null;
        _ = await ordenes.ObtenerAsync(ordenId, tecnico, cancellationToken) ?? throw new ErrorNoEncontrado("La orden de trabajo");
        var guardada = await intervenciones.ObtenerAsync(ordenId, cancellationToken) ?? throw new ErrorNoEncontrado("La intervención");
        return IntervencionDto.Desde(guardada.Intervencion);
    }

    private async Task<OrdenTrabajo> PropiaAsync(Guid id, CancellationToken cancellationToken)
    {
        var orden = await ordenes.OrdenAsync(id, cancellationToken);
        return orden is not null && orden.TecnicoId == contexto.UsuarioId
            ? orden
            : throw new ErrorNoEncontrado("La orden de trabajo");
    }

    private static string MensajeTransicion(string accion, string estado) => accion == AccionesOrden.Pausar
        ? $"Solo puedes pausar una orden en ejecución; esta está «{estado}»."
        : $"Solo puedes iniciar una orden asignada o pausada; esta está «{estado}».";

    private static (string Diagnostico, string Acciones, IReadOnlyList<Repuesto> Repuestos, string? Salida) Validar(
        DatosIntervencion datos, IDictionary<string, string[]> errores)
    {
        var diagnostico = Texto(datos.Diagnostico, "diagnostico", "Describe el diagnóstico del equipo.", errores);
        var acciones = Texto(datos.AccionesRealizadas, "accionesRealizadas", "Describe las acciones realizadas.", errores);

        var repuestos = new List<Repuesto>();
        var recibidos = datos.Repuestos ?? [];
        if (recibidos.Count > MaximoRepuestos)
        {
            errores["repuestos"] = [$"Registra máximo {MaximoRepuestos} repuestos."];
        }
        else
        {
            for (var i = 0; i < recibidos.Count; i++)
            {
                var r = recibidos[i];
                var nombre = r?.Nombre?.Trim();
                if (string.IsNullOrEmpty(nombre) || nombre.Length > LongitudMaximaRepuesto)
                {
                    errores[$"repuestos[{i}].nombre"] = [$"Escribe el nombre del repuesto (máximo {LongitudMaximaRepuesto} caracteres)."];
                }

                if (r?.Cantidad is not (>= 1 and <= CantidadMaxima))
                {
                    errores[$"repuestos[{i}].cantidad"] = [$"La cantidad va de 1 a {CantidadMaxima}."];
                }

                if (r?.CostoUnitario < 0)
                {
                    errores[$"repuestos[{i}].costoUnitario"] = ["El costo unitario no puede ser negativo."];
                }

                if (r is not null && !string.IsNullOrEmpty(nombre) && r.Cantidad is { } cantidad)
                {
                    repuestos.Add(new Repuesto(nombre, cantidad, r.CostoUnitario));
                }
            }
        }

        if (datos.CostoManoObra < 0)
        {
            errores["costoManoObra"] = ["El costo de mano de obra no puede ser negativo."];
        }

        if (datos.TiempoAtencionMin is { } minutos && minutos is < 1 or > TiempoMaximoMin)
        {
            errores["tiempoAtencionMin"] = [$"El tiempo de atención va de 1 a {TiempoMaximoMin} minutos."];
        }

        string? salida = null;
        if (!string.IsNullOrWhiteSpace(datos.EstadoEquipoSalida))
        {
            salida = EstadosSalidaEquipo.Todos.FirstOrDefault(e => string.Equals(e, datos.EstadoEquipoSalida.Trim(), StringComparison.OrdinalIgnoreCase));
            if (salida is null)
            {
                errores["estadoEquipoSalida"] = [$"El estado de salida debe ser {string.Join(", ", EstadosSalidaEquipo.Todos)}."];
            }
        }

        return (diagnostico, acciones, repuestos, salida);
    }

    private static string Texto(string? valor, string campo, string mensaje, IDictionary<string, string[]> errores)
    {
        var texto = valor?.Trim() ?? string.Empty;
        if (texto.Length == 0)
        {
            errores[campo] = [mensaje];
        }
        else if (texto.Length > LongitudMaximaTexto)
        {
            errores[campo] = [$"Máximo {LongitudMaximaTexto} caracteres."];
        }

        return texto;
    }
}
