using System.Globalization;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Mantenimiento;

/// <summary>
/// Calibraciones de los equipos (HU-022, RF-022; API-CAL). Los permisos (registra y corrige AC-03; aprueba
/// AC-02; consultan AC-01, AC-02, AC-03, AC-05 y AC-07) los exige la API. Solo se calibran equipos que
/// requieren calibración (RN-010); el vencimiento es la fecha + la periodicidad del equipo salvo que se
/// indique. La vigencia que bloquea el cierre «Operativo» (RN-007) y las alertas (HU-020) salen de aquí.
/// </summary>
public sealed class GestionCalibraciones(
    IRepositorioCalibraciones repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = "calibracion";

    /// <summary>El equipo no requiere calibración (RN-010).</summary>
    public const string CodigoNoRequiere = "CAL-001";

    /// <summary>El equipo está dado de baja.</summary>
    public const string CodigoEquipoDeBaja = "CAL-002";

    /// <summary>La calibración ya está aprobada y no cambia.</summary>
    public const string CodigoAprobada = "CAL-003";

    public async Task<Pagina<CalibracionDto>> ListarAsync(FiltroCalibraciones filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<CalibracionDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<CalibracionDto>.TamanoMaximo}.");
        }

        var (items, total) = await repositorio.ListarAsync(filtro, cancellationToken);
        var hoy = FechaColombia.Hoy(tiempo);
        return new Pagina<CalibracionDto>(items.Select(c => CalibracionDto.Desde(c, hoy)).ToList(), filtro.Pagina, filtro.Tamano, total);
    }

    public async Task<CalibracionDto> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var leida = await repositorio.ObtenerAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("La calibración");
        return CalibracionDto.Desde(leida, FechaColombia.Hoy(tiempo));
    }

    /// <summary>CA-HU-022-01: la calibración completa queda en el historial del equipo con su vencimiento.</summary>
    public async Task<CalibracionDto> RegistrarAsync(DatosCalibracion datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = new Errores();
        if (datos.EquipoId is null || datos.EquipoId == Guid.Empty)
        {
            errores.Agregar("equipoId", "Selecciona el equipo calibrado.");
        }

        var campos = Campos(datos.FechaCalibracion, datos.PatronReferencia, datos.Resultado ?? RegistroCalibracion.Conforme, datos.NumeroCertificado, errores);
        errores.Lanzar();

        var equipo = await EquipoCalibrableAsync(datos.EquipoId!.Value, cancellationToken);
        await ExigirProveedorAsync(datos.ProveedorId, cancellationToken);
        var vencimiento = Vencimiento(campos.Fecha, datos.FechaVencimiento, equipo);

        var calibracion = new RegistroCalibracion(
            contexto.TenantId,
            equipo.Id,
            contexto.UsuarioId,
            campos.Fecha,
            campos.Patron,
            campos.Resultado,
            vencimiento,
            campos.Certificado,
            datos.ProveedorId);
        repositorio.Agregar(calibracion);
        await repositorio.GuardarAsync(cancellationToken);
        await Auditar(AccionesAuditoria.Crear, calibracion, equipo.PlacaInventario, cancellationToken);
        return await ObtenerAsync(calibracion.Id, cancellationToken);
    }

    /// <summary>Corrige una calibración aún sin aprobar: lo que no se envía no cambia.</summary>
    public async Task<CalibracionDto> CorregirAsync(Guid id, DatosCorreccionCalibracion datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var calibracion = await SinAprobarAsync(id, cancellationToken);
        var errores = new Errores();
        var campos = Campos(
            datos.FechaCalibracion ?? calibracion.FechaCalibracion,
            datos.PatronReferencia ?? calibracion.PatronReferencia,
            datos.Resultado ?? calibracion.Resultado,
            datos.NumeroCertificado ?? calibracion.NumeroCertificado,
            errores);
        errores.Lanzar();

        var equipo = await repositorio.EquipoAsync(calibracion.EquipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var proveedor = datos.ProveedorId is null ? calibracion.ProveedorId : datos.ProveedorId == Guid.Empty ? null : datos.ProveedorId;
        await ExigirProveedorAsync(proveedor, cancellationToken);
        var vencimiento = Vencimiento(
            campos.Fecha,
            datos.FechaVencimiento ?? (datos.FechaCalibracion is null ? calibracion.FechaVencimiento : null),
            equipo);

        calibracion.Corregir(campos.Fecha, campos.Patron, campos.Resultado, vencimiento, campos.Certificado, proveedor);
        await repositorio.GuardarAsync(cancellationToken);
        await Auditar(AccionesAuditoria.Actualizar, calibracion, equipo.PlacaInventario, cancellationToken);
        return await ObtenerAsync(calibracion.Id, cancellationToken);
    }

    /// <summary>El Ingeniero aprueba el registro (API-CAL); aprobado, ya no se corrige.</summary>
    public async Task<CalibracionDto> AprobarAsync(Guid id, CancellationToken cancellationToken)
    {
        var calibracion = await SinAprobarAsync(id, cancellationToken);
        calibracion.Aprobar(contexto.UsuarioId);
        await repositorio.GuardarAsync(cancellationToken);
        var leida = await repositorio.ObtenerAsync(calibracion.Id, cancellationToken) ?? throw new ErrorNoEncontrado("La calibración");
        await Auditar(AccionesAuditoria.Actualizar, calibracion, leida.PlacaInventario, cancellationToken);
        return CalibracionDto.Desde(leida, FechaColombia.Hoy(tiempo));
    }

    private async Task<RegistroCalibracion> SinAprobarAsync(Guid id, CancellationToken cancellationToken)
    {
        var calibracion = await repositorio.CalibracionAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("La calibración");
        return calibracion.Aprobada
            ? throw new ErrorConflicto(CodigoAprobada, "La calibración ya está aprobada; no se puede modificar.")
            : calibracion;
    }

    /// <summary>CA-HU-022-03: un equipo que no requiere calibración no se calibra (RN-010).</summary>
    private async Task<EquipoDeCalibracion> EquipoCalibrableAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        var equipo = await repositorio.EquipoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        if (equipo.DadoDeBaja)
        {
            throw new ErrorConflicto(CodigoEquipoDeBaja, $"El equipo {equipo.PlacaInventario} está dado de baja; no se le registran calibraciones.");
        }

        if (!equipo.RequiereCalibracion)
        {
            throw new ErrorConflicto(CodigoNoRequiere, $"El equipo {equipo.PlacaInventario} no requiere calibración (RN-010).");
        }

        return equipo;
    }

    private async Task ExigirProveedorAsync(Guid? proveedorId, CancellationToken cancellationToken)
    {
        if (proveedorId is { } id && id != Guid.Empty && !await repositorio.ExisteProveedorAsync(id, cancellationToken))
        {
            throw ErrorValidacion.DeCampo("proveedorId", "El laboratorio no existe en la institución.");
        }
    }

    /// <summary>El indicado, o la fecha + la periodicidad del equipo; nunca antes de la fecha.</summary>
    private static DateOnly Vencimiento(DateOnly fecha, DateOnly? indicado, EquipoDeCalibracion equipo)
    {
        if (indicado is { } vence)
        {
            return vence >= fecha
                ? vence
                : throw ErrorValidacion.DeCampo("fechaVencimiento", "El vencimiento no puede ser anterior a la fecha de la calibración.");
        }

        return equipo.PeriodicidadMeses is { } meses
            ? RegistroCalibracion.VencimientoPorPeriodicidad(fecha, meses)
            : throw ErrorValidacion.DeCampo("fechaVencimiento", "El equipo no tiene periodicidad de calibración; indique el vencimiento.");
    }

    private (DateOnly Fecha, string Patron, string Resultado, string? Certificado) Campos(
        DateOnly? fecha,
        string? patron,
        string resultado,
        string? certificado,
        Errores errores)
    {
        if (fecha is null)
        {
            errores.Agregar("fechaCalibracion", "Indique la fecha de la calibración.");
        }
        else if (fecha > FechaColombia.Hoy(tiempo))
        {
            errores.Agregar("fechaCalibracion", "La fecha de la calibración no puede ser futura.");
        }

        var textoPatron = patron?.Trim();
        if (string.IsNullOrEmpty(textoPatron))
        {
            errores.Agregar("patronReferencia", "Indique el patrón de referencia.");
        }
        else if (textoPatron.Length > RegistroCalibracion.LongitudMaximaPatron)
        {
            errores.Agregar("patronReferencia", $"Máximo {RegistroCalibracion.LongitudMaximaPatron} caracteres.");
        }

        var textoResultado = resultado.Trim().ToLowerInvariant();
        if (!RegistroCalibracion.Resultados.Contains(textoResultado))
        {
            errores.Agregar("resultado", "El resultado debe ser conforme o no conforme.");
        }

        var textoCertificado = string.IsNullOrWhiteSpace(certificado) ? null : certificado.Trim();
        if (textoCertificado is { Length: > RegistroCalibracion.LongitudMaximaCertificado })
        {
            errores.Agregar("numeroCertificado", $"Máximo {RegistroCalibracion.LongitudMaximaCertificado} caracteres.");
        }

        return (fecha ?? default, textoPatron ?? string.Empty, textoResultado, textoCertificado);
    }

    private Task Auditar(string accion, RegistroCalibracion calibracion, string placa, CancellationToken cancellationToken) =>
        auditoria.RegistrarAsync(
            contexto.UsuarioId,
            accion,
            Entidad,
            calibracion.Id.ToString(),
            new Dictionary<string, string>
            {
                ["equipo"] = placa,
                ["fechaCalibracion"] = calibracion.FechaCalibracion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["fechaVencimiento"] = calibracion.FechaVencimiento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["resultado"] = calibracion.Resultado,
                ["aprobada"] = calibracion.Aprobada ? "sí" : "no",
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
