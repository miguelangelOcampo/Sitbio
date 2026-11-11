using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Inventario;

/// <summary>
/// Equipo biomédico y su hoja de vida (tabla <c>equipo_biomedico</c>, script 01 + V008; HU-002).
/// Serie y placa son únicas dentro de la institución (RN-001: <c>uq_equipo_tenant_serie</c> y
/// <c>uq_equipo_tenant_placa</c>). La información regulatoria sigue RF-030 y RN-010.
/// </summary>
public sealed class EquipoBiomedico : EntidadTenant
{
    public const int LongitudMaximaSerie = 50;
    public const int LongitudMaximaPlaca = 50;
    public const int LongitudMaximaMarca = 80;
    public const int LongitudMaximaModelo = 80;
    public const int LongitudMaximaRegistro = 40;

    // Constructor para EF Core.
    private EquipoBiomedico(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Alta del equipo: nace «Operativo» (CA-HU-002-01).</summary>
    public EquipoBiomedico(
        Guid tenantId,
        Guid tipoEquipoId,
        Guid ubicacionId,
        string numeroSerie,
        string placaInventario,
        string marca,
        string modelo,
        DateTimeOffset fechaCreacion,
        Guid? proveedorId = null,
        DateOnly? fechaAdquisicion = null)
        : base(tenantId)
    {
        EstadoEquipoId = EstadosEquipo.Operativo;
        FechaCreacion = fechaCreacion;
        RequiereCalibracion = true;
        PeriodicidadCalibracionMeses = 12;
        ActualizarDatos(tipoEquipoId, ubicacionId, numeroSerie, placaInventario, marca, modelo, proveedorId, fechaAdquisicion);
    }

    public Guid TipoEquipoId { get; private set; }

    public Guid UbicacionId { get; private set; }

    public short EstadoEquipoId { get; private set; }

    public Guid? ProveedorId { get; private set; }

    public string NumeroSerie { get; private set; } = string.Empty;

    public string PlacaInventario { get; private set; } = string.Empty;

    public string Marca { get; private set; } = string.Empty;

    public string Modelo { get; private set; } = string.Empty;

    public DateOnly? FechaAdquisicion { get; private set; }

    public DateTimeOffset? FechaBaja { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    // ---------- Información regulatoria (RF-030, RN-010; V008) ----------

    public string? TipoAutorizacion { get; private set; }

    public string? RegistroInvima { get; private set; }

    public DateOnly? VenceRegistro { get; private set; }

    public string? ClaseRiesgo { get; private set; }

    public bool RequiereCalibracion { get; private set; }

    public short? PeriodicidadCalibracionMeses { get; private set; }

    /// <summary>Fotografía opcional (RF-031); se usa con HU-015.</summary>
    public Guid? FotoDocumentoId { get; private set; }

    public bool DadoDeBaja => FechaBaja is not null;

    /// <summary>Datos de identificación y ubicación. Serie y placa se guardan sin espacios a los lados.</summary>
    public void ActualizarDatos(
        Guid tipoEquipoId,
        Guid ubicacionId,
        string numeroSerie,
        string placaInventario,
        string marca,
        string modelo,
        Guid? proveedorId,
        DateOnly? fechaAdquisicion)
    {
        TipoEquipoId = Requerido(tipoEquipoId, nameof(tipoEquipoId));
        UbicacionId = Requerido(ubicacionId, nameof(ubicacionId));
        NumeroSerie = Texto.Requerido(numeroSerie, nameof(numeroSerie), LongitudMaximaSerie);
        PlacaInventario = Texto.Requerido(placaInventario, nameof(placaInventario), LongitudMaximaPlaca);
        Marca = Texto.Requerido(marca, nameof(marca), LongitudMaximaMarca);
        Modelo = Texto.Requerido(modelo, nameof(modelo), LongitudMaximaModelo);
        ProveedorId = proveedorId == Guid.Empty ? null : proveedorId;
        FechaAdquisicion = fechaAdquisicion;
    }

    /// <summary>
    /// RN-010: autorización INVIMA con número y vigencia, clase de riesgo y calibración.
    /// Si no requiere calibración, la periodicidad queda vacía (CHECK de la V008).
    /// </summary>
    public void DefinirInformacionRegulatoria(
        string tipoAutorizacion,
        string registroInvima,
        DateOnly venceRegistro,
        string claseRiesgo,
        bool requiereCalibracion,
        int? periodicidadMeses)
    {
        if (!TiposAutorizacion.EsValido(tipoAutorizacion))
        {
            throw new ArgumentException("El tipo de autorización debe ser registro sanitario o permiso de comercialización.", nameof(tipoAutorizacion));
        }

        if (!ClasesRiesgo.EsValida(claseRiesgo))
        {
            throw new ArgumentException("La clase de riesgo debe ser I, IIa, IIb o III.", nameof(claseRiesgo));
        }

        var periodicidad = Calibracion.Periodicidad(requiereCalibracion, periodicidadMeses);
        TipoAutorizacion = tipoAutorizacion;
        RegistroInvima = Texto.Requerido(registroInvima, nameof(registroInvima), LongitudMaximaRegistro);
        VenceRegistro = venceRegistro;
        ClaseRiesgo = claseRiesgo;
        RequiereCalibracion = requiereCalibracion;
        PeriodicidadCalibracionMeses = periodicidad;
    }

    /// <summary>RN-010: aviso (no bloqueo) si la autorización venció o le faltan 180 días o menos.</summary>
    public AvisoRegistro AvisoDelRegistro(DateOnly hoy) => Calibracion.Aviso(VenceRegistro, hoy);

    // ---------- Estado operativo y baja lógica (RF-014, HU-014) ----------

    /// <summary>
    /// Cambia el estado con su motivo y devuelve el registro para el historial (CA-HU-014-01).
    /// Pasar a «Baja» fija la fecha de baja; la hoja de vida y el historial se conservan
    /// (CA-HU-014-02). Un equipo dado de baja solo sale de ese estado con <see cref="RevertirBaja"/>.
    /// </summary>
    public HistorialEstadoEquipo CambiarEstado(short nuevoEstadoId, string? motivo, Guid usuarioId, DateTimeOffset momento)
    {
        if (!EstadosEquipo.EsValido(nuevoEstadoId))
        {
            throw new ArgumentException("El estado indicado no existe.", nameof(nuevoEstadoId));
        }

        if (DadoDeBaja)
        {
            throw new InvalidOperationException("El equipo está dado de baja: primero revierte la baja.");
        }

        if (nuevoEstadoId == EstadoEquipoId)
        {
            throw new InvalidOperationException("El equipo ya está en ese estado.");
        }

        return Registrar(nuevoEstadoId, motivo, usuarioId, momento, nuevoEstadoId == EstadosEquipo.Baja ? momento : null);
    }

    /// <summary>Baja lógica: deja de figurar como activo y no consume capacidad del plan (RN-009).</summary>
    public HistorialEstadoEquipo DarDeBaja(string? motivo, Guid usuarioId, DateTimeOffset momento) =>
        CambiarEstado(EstadosEquipo.Baja, motivo, usuarioId, momento);

    /// <summary>Reversión de la baja por AC-02 (DOC-06, Tabla 29): el equipo vuelve a «Operativo».</summary>
    public HistorialEstadoEquipo RevertirBaja(string? motivo, Guid usuarioId, DateTimeOffset momento)
    {
        if (!DadoDeBaja)
        {
            throw new InvalidOperationException("El equipo no está dado de baja.");
        }

        return Registrar(EstadosEquipo.Operativo, motivo, usuarioId, momento, null);
    }

    private HistorialEstadoEquipo Registrar(short nuevoEstadoId, string? motivo, Guid usuarioId, DateTimeOffset momento, DateTimeOffset? fechaBaja)
    {
        // Se valida todo antes de cambiar el estado: si algo falla, el equipo queda como estaba.
        var motivoLimpio = Texto.Requerido(motivo, nameof(motivo), HistorialEstadoEquipo.LongitudMaximaMotivo);
        var registro = new HistorialEstadoEquipo(TenantId, Id, EstadoEquipoId, nuevoEstadoId, usuarioId, motivoLimpio, momento);
        EstadoEquipoId = nuevoEstadoId;
        FechaBaja = fechaBaja;
        return registro;
    }

    private static Guid Requerido(Guid id, string campo) =>
        id != Guid.Empty ? id : throw new ArgumentException($"El campo {campo} es obligatorio.", campo);
}
