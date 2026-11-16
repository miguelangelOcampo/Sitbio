using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>
/// Inventario de equipos (HU-002; RF-003, RF-030; RN-001, RN-009, RN-010). Los permisos
/// (consulta AC-01 a AC-05 y AC-07; registro y edición solo AC-02) los exige la API; aquí se
/// aplican las reglas de negocio. El tenant sale siempre del token (<see cref="IContextoUsuario"/>).
/// </summary>
public sealed class GestionEquipos(
    IRepositorioEquipos repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string CodigoDuplicado = "RN-001";
    public const string CodigoLimitePlan = "RN-009";
    public const string CodigoMismoEstado = "EQ-001";
    public const string CodigoBajaSoloOperativo = "EQ-002";
    public const string Entidad = "equipo_biomedico";
    public const string DescripcionPreventivo = "Mantenimiento preventivo";

    private const string Obligatorio = "Este dato es obligatorio.";

    public async Task<Pagina<EquipoDto>> ListarAsync(FiltroEquipos filtro, CancellationToken cancellationToken)
    {
        if (filtro.Pagina < 1 || filtro.Tamano < 1 || filtro.Tamano > Pagina<EquipoDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<EquipoDto>.TamanoMaximo}.");
        }

        var clase = Limpio(filtro.Clase);
        if (clase is not null && !ClasesRiesgo.EsValida(clase))
        {
            throw ErrorValidacion.DeCampo("clase", "La clase de riesgo debe ser I, IIa, IIb o III.");
        }

        var estado = Limpio(filtro.Estado)?.ToUpperInvariant();
        if (estado is not null && !EstadosEquipo.Codigos.Contains(estado))
        {
            throw ErrorValidacion.DeCampo("estado", $"El estado debe ser {string.Join(", ", EstadosEquipo.Codigos)}.");
        }

        var limpio = filtro with { Placa = Limpio(filtro.Placa), Serie = Limpio(filtro.Serie), Estado = estado, Clase = clase };
        var (items, total) = await repositorio.ListarAsync(limpio, cancellationToken);
        var hoy = FechaColombia.Hoy(tiempo);
        return new Pagina<EquipoDto>(items.Select(i => EquipoDto.Desde(i, hoy)).ToList(), filtro.Pagina, filtro.Tamano, total);
    }

    public async Task<EquipoDto> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var leido = await repositorio.ObtenerAsync(id, paraEditar: false, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        return EquipoDto.Desde(leido, FechaColombia.Hoy(tiempo));
    }

    /// <summary>Registra el equipo «Operativo» (CA-HU-002-01) y, si se indica, su plan preventivo (CA-HU-002-06).</summary>
    public async Task<EquipoDto> CrearAsync(DatosEquipo datos, CancellationToken cancellationToken)
    {
        await ExigirCupoDeEquipos(cancellationToken);
        var hoy = FechaColombia.Hoy(tiempo);
        var v = await Validar(datos, null, hoy, cancellationToken);
        await ExigirSinDuplicados(v.Serie, v.Placa, null, cancellationToken);

        var ahora = tiempo.GetUtcNow();
        var equipo = Construir(() =>
        {
            var nuevo = new EquipoBiomedico(
                contexto.TenantId, v.Tipo.Id, v.Ubicacion.Id, v.Serie, v.Placa, v.Marca, v.Modelo, ahora, v.Proveedor?.Id, datos.FechaAdquisicion);
            nuevo.DefinirInformacionRegulatoria(v.TipoAutorizacion, v.Registro, v.Vence, v.Clase, v.RequiereCalibracion, v.Periodicidad);
            return nuevo;
        });
        var plan = v.Plan is { } p
            ? Construir(() => new PlanPreventivo(contexto.TenantId, equipo.Id, p.Descripcion, p.Dias, p.Proxima, ahora, p.Ultima))
            : null;

        repositorio.Agregar(equipo);
        repositorio.Agregar(HistorialEstadoEquipo.Alta(equipo, contexto.UsuarioId));
        if (plan is not null)
        {
            repositorio.Agregar(plan);
        }

        await repositorio.GuardarAsync(cancellationToken);
        await Auditar(AccionesAuditoria.Crear, equipo, v.Tipo, cancellationToken);

        var leido = new EquipoLeido(equipo, v.Tipo.Nombre, v.Ubicacion.Nombre, v.Proveedor?.Nombre, EstadosEquipo.CodigoOperativo, EstadosEquipo.NombreOperativo, plan);
        return EquipoDto.Desde(leido, hoy);
    }

    /// <summary>Edita el equipo; RN-010 se exige también al editar. Editar no consume capacidad del plan (RN-009).</summary>
    public async Task<EquipoDto> ActualizarAsync(Guid id, DatosEquipo datos, CancellationToken cancellationToken)
    {
        var leido = await repositorio.ObtenerAsync(id, paraEditar: true, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var equipo = leido.Equipo;
        var hoy = FechaColombia.Hoy(tiempo);
        var v = await Validar(datos, equipo, hoy, cancellationToken);
        await ExigirSinDuplicados(v.Serie, v.Placa, equipo.Id, cancellationToken);

        Construir(() =>
        {
            equipo.ActualizarDatos(v.Tipo.Id, v.Ubicacion.Id, v.Serie, v.Placa, v.Marca, v.Modelo, v.Proveedor?.Id, datos.FechaAdquisicion);
            equipo.DefinirInformacionRegulatoria(v.TipoAutorizacion, v.Registro, v.Vence, v.Clase, v.RequiereCalibracion, v.Periodicidad);
            return equipo;
        });

        var plan = leido.Plan;
        if (v.Plan is { } p)
        {
            if (plan is null)
            {
                plan = Construir(() => new PlanPreventivo(contexto.TenantId, equipo.Id, p.Descripcion, p.Dias, p.Proxima, tiempo.GetUtcNow(), p.Ultima));
                repositorio.Agregar(plan);
            }
            else
            {
                var existente = plan;
                Construir(() =>
                {
                    existente.Programar(p.Dias, p.Proxima, p.Ultima);
                    return existente;
                });
            }
        }

        await repositorio.GuardarAsync(cancellationToken);
        await Auditar(AccionesAuditoria.Actualizar, equipo, v.Tipo, cancellationToken);

        var actualizado = leido with { TipoEquipo = v.Tipo.Nombre, Ubicacion = v.Ubicacion.Nombre, Proveedor = v.Proveedor?.Nombre, Plan = plan };
        return EquipoDto.Desde(actualizado, hoy);
    }

    /// <summary>
    /// Cambia el estado operativo con su motivo (RF-014, CA-HU-014-01), da de baja de forma lógica
    /// (CA-HU-014-02) o revierte la baja cuando se pide «OPERATIVO» para un equipo dado de baja.
    /// Revertir vuelve a consumir capacidad, por eso exige cupo en el plan (RN-009).
    /// </summary>
    public async Task<EquipoDto> CambiarEstadoAsync(Guid id, DatosCambioEstado datos, CancellationToken cancellationToken)
    {
        var codigo = Limpio(datos.Estado)?.ToUpperInvariant();
        var nuevoEstado = codigo is null ? null : EstadosEquipo.IdDe(codigo);
        if (nuevoEstado is not { } estado)
        {
            throw ErrorValidacion.DeCampo("estado", $"El estado debe ser {string.Join(", ", EstadosEquipo.Codigos)}.");
        }

        var leido = await repositorio.ObtenerAsync(id, paraEditar: true, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var equipo = leido.Equipo;
        var anterior = leido.EstadoCodigo;
        var ahora = tiempo.GetUtcNow();
        string cambio;
        HistorialEstadoEquipo registro;

        if (equipo.DadoDeBaja)
        {
            if (estado != EstadosEquipo.Operativo)
            {
                throw new ErrorConflicto(CodigoBajaSoloOperativo, "El equipo está dado de baja: solo puede volver a «Operativo» (reversión de la baja).");
            }

            await ExigirCupoDeEquipos(cancellationToken);
            registro = Construir(() => equipo.RevertirBaja(datos.Motivo, contexto.UsuarioId, ahora));
            cambio = "reversion_baja";
        }
        else
        {
            if (estado == equipo.EstadoEquipoId)
            {
                throw new ErrorConflicto(CodigoMismoEstado, $"El equipo ya está en estado «{leido.EstadoNombre}».");
            }

            registro = Construir(() => equipo.CambiarEstado(estado, datos.Motivo, contexto.UsuarioId, ahora));
            cambio = estado == EstadosEquipo.Baja ? "baja" : "estado";
        }

        repositorio.Agregar(registro);
        await repositorio.GuardarAsync(cancellationToken);

        // El motivo es texto libre: queda en el historial, no en la bitácora (sin datos personales).
        var detalle = new Dictionary<string, string>
        {
            ["placa"] = equipo.PlacaInventario,
            ["cambio"] = cambio,
            ["estado_anterior"] = anterior,
            ["estado_nuevo"] = codigo!,
        };
        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Actualizar, Entidad, equipo.Id.ToString(), detalle, cancellationToken);

        return await ObtenerAsync(id, cancellationToken);
    }

    /// <summary>Historial de estados del equipo, del más reciente al más antiguo (404 si no es del tenant).</summary>
    public async Task<IReadOnlyList<CambioEstadoDto>> HistorialAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = await repositorio.ObtenerAsync(id, paraEditar: false, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var historial = await repositorio.HistorialAsync(id, cancellationToken);
        return historial.Select(CambioEstadoDto.Desde).ToList();
    }

    /// <summary>
    /// RN-009 (CA-HU-002-03): con el máximo de equipos activos del plan no se registran equipos
    /// nuevos (409). Consultar y editar los existentes sigue permitido.
    /// </summary>
    private async Task ExigirCupoDeEquipos(CancellationToken cancellationToken)
    {
        var capacidad = await repositorio.CapacidadEquiposAsync(cancellationToken);
        if (capacidad.Llena)
        {
            throw new ErrorConflicto(
                CodigoLimitePlan,
                $"Tu institución alcanzó el máximo de equipos activos del plan {capacidad.Plan} ({capacidad.Activos} de {capacidad.Maximo}). "
                + "Puedes seguir consultando y editando los equipos registrados; para ampliar la capacidad, comunícate con SITBIO.");
        }
    }

    /// <summary>RN-001 (CA-HU-002-02): serie y placa únicas dentro de la institución.</summary>
    private async Task ExigirSinDuplicados(string serie, string placa, Guid? excepto, CancellationToken cancellationToken)
    {
        var duplicados = await repositorio.DuplicadosAsync(serie, placa, excepto, cancellationToken);
        if (duplicados.Placa)
        {
            throw new ErrorConflicto(CodigoDuplicado, $"Ya existe un equipo con la placa de inventario {placa} en tu institución.");
        }

        if (duplicados.Serie)
        {
            throw new ErrorConflicto(CodigoDuplicado, $"Ya existe un equipo con el número de serie {serie} en tu institución.");
        }
    }

    /// <summary>
    /// Bitácora: quién registró o editó el equipo y, si la clase o la calibración difieren de los
    /// valores del tipo, cuáles (decisión de AC-02, RN-010). Sin datos personales.
    /// </summary>
    private Task Auditar(string accion, EquipoBiomedico equipo, TipoEquipo tipo, CancellationToken cancellationToken)
    {
        var detalle = new Dictionary<string, string> { ["placa"] = equipo.PlacaInventario };
        var diferencias = DiferenciasConElTipo(equipo, tipo);
        if (diferencias.Count > 0)
        {
            detalle["difiere_del_tipo"] = string.Join(",", diferencias);
        }

        return auditoria.RegistrarAsync(contexto.UsuarioId, accion, Entidad, equipo.Id.ToString(), detalle, cancellationToken);
    }

    public static IReadOnlyList<string> DiferenciasConElTipo(EquipoBiomedico equipo, TipoEquipo tipo)
    {
        var diferencias = new List<string>();
        if (tipo.ClaseRiesgoDefecto is not null && tipo.ClaseRiesgoDefecto != equipo.ClaseRiesgo)
        {
            diferencias.Add("clase_riesgo");
        }

        if (tipo.RequiereCalibracion != equipo.RequiereCalibracion)
        {
            diferencias.Add("requiere_calibracion");
        }
        else if (equipo.RequiereCalibracion && tipo.PeriodicidadCalibracionMeses != equipo.PeriodicidadCalibracionMeses)
        {
            diferencias.Add("periodicidad_calibracion_meses");
        }

        return diferencias;
    }

    private sealed record PlanCalculado(int Dias, DateOnly? Ultima, DateOnly Proxima, string Descripcion);

    private sealed record Validados(
        TipoEquipo Tipo,
        ElementoCatalogo Ubicacion,
        ElementoCatalogo? Proveedor,
        string Serie,
        string Placa,
        string Marca,
        string Modelo,
        string TipoAutorizacion,
        string Registro,
        DateOnly Vence,
        string Clase,
        bool RequiereCalibracion,
        int? Periodicidad,
        PlanCalculado? Plan);

    /// <summary>Valida todos los campos a la vez para responder 400 con cada error en su campo.</summary>
    private async Task<Validados> Validar(DatosEquipo datos, EquipoBiomedico? actual, DateOnly hoy, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, List<string>>();
        void Error(string campo, string mensaje)
        {
            if (!errores.TryGetValue(campo, out var lista))
            {
                errores[campo] = lista = [];
            }

            lista.Add(mensaje);
        }

        string Texto(string campo, string? valor, int maximo)
        {
            var limpio = valor?.Trim() ?? string.Empty;
            if (limpio.Length == 0)
            {
                Error(campo, Obligatorio);
            }
            else if (limpio.Length > maximo)
            {
                Error(campo, $"Máximo {maximo} caracteres.");
            }

            return limpio;
        }

        // Catálogos de la institución: un tipo o una ubicación inactivos solo se aceptan si el equipo ya los tenía.
        TipoEquipo? tipo = null;
        if (datos.TipoEquipoId is not { } tipoId || tipoId == Guid.Empty)
        {
            Error("tipoEquipoId", Obligatorio);
        }
        else
        {
            tipo = await repositorio.TipoEquipoAsync(tipoId, cancellationToken);
            if (tipo is null)
            {
                Error("tipoEquipoId", "El tipo de equipo no existe.");
            }
            else if (!tipo.EstadoActivo && actual?.TipoEquipoId != tipoId)
            {
                Error("tipoEquipoId", "El tipo de equipo está inactivo.");
            }
        }

        ElementoCatalogo? ubicacion = null;
        if (datos.UbicacionId is not { } ubicacionId || ubicacionId == Guid.Empty)
        {
            Error("ubicacionId", Obligatorio);
        }
        else
        {
            ubicacion = await repositorio.UbicacionAsync(ubicacionId, cancellationToken);
            if (ubicacion is null)
            {
                Error("ubicacionId", "La ubicación no existe.");
            }
            else if (!ubicacion.Activo && actual?.UbicacionId != ubicacionId)
            {
                Error("ubicacionId", "La ubicación está inactiva.");
            }
        }

        ElementoCatalogo? proveedor = null;
        if (datos.ProveedorId is { } proveedorId && proveedorId != Guid.Empty)
        {
            proveedor = await repositorio.ProveedorAsync(proveedorId, cancellationToken);
            if (proveedor is null)
            {
                Error("proveedorId", "El proveedor no existe.");
            }
        }

        var serie = Texto("numeroSerie", datos.NumeroSerie, EquipoBiomedico.LongitudMaximaSerie);
        var placa = Texto("placaInventario", datos.PlacaInventario, EquipoBiomedico.LongitudMaximaPlaca);
        var marca = Texto("marca", datos.Marca, EquipoBiomedico.LongitudMaximaMarca);
        var modelo = Texto("modelo", datos.Modelo, EquipoBiomedico.LongitudMaximaModelo);

        // RN-010: autorización INVIMA con número y vigencia, clase de riesgo y calibración.
        if (!TiposAutorizacion.EsValido(datos.TipoAutorizacion))
        {
            Error("tipoAutorizacion", "Elija registro sanitario o permiso de comercialización.");
        }

        var registro = Texto("registroInvima", datos.RegistroInvima, EquipoBiomedico.LongitudMaximaRegistro);
        if (datos.VenceRegistro is null)
        {
            Error("venceRegistro", Obligatorio);
        }

        if (!ClasesRiesgo.EsValida(datos.ClaseRiesgo))
        {
            Error("claseRiesgo", "La clase de riesgo debe ser I, IIa, IIb o III.");
        }

        int? periodicidad = null;
        if (datos.RequiereCalibracion is null)
        {
            Error("requiereCalibracion", Obligatorio);
        }
        else if (datos.RequiereCalibracion.Value)
        {
            periodicidad = datos.PeriodicidadCalibracionMeses;
            if (periodicidad is not (>= Calibracion.PeriodicidadMinimaMeses and <= Calibracion.PeriodicidadMaximaMeses))
            {
                Error(
                    "periodicidadCalibracionMeses",
                    $"Si requiere calibración, la periodicidad va de {Calibracion.PeriodicidadMinimaMeses} a {Calibracion.PeriodicidadMaximaMeses} meses.");
            }
        }

        var plan = datos.Preventivo is null ? null : ValidarPreventivo(datos.Preventivo, hoy, Error);

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        return new Validados(
            tipo!,
            ubicacion!,
            proveedor,
            serie,
            placa,
            marca,
            modelo,
            datos.TipoAutorizacion!,
            registro,
            datos.VenceRegistro!.Value,
            datos.ClaseRiesgo!,
            datos.RequiereCalibracion!.Value,
            periodicidad,
            plan);
    }

    /// <summary>CA-HU-002-06: próximo = último + periodicidad (ajustable); sin último, el próximo no puede haber pasado.</summary>
    private static PlanCalculado? ValidarPreventivo(DatosPreventivo datos, DateOnly hoy, Action<string, string> error)
    {
        const string Campo = "preventivo.";
        var valido = true;
        if (datos.PeriodicidadDias is < 1 or > short.MaxValue)
        {
            error(Campo + "periodicidadDias", "La periodicidad del preventivo debe ser de al menos un día.");
            valido = false;
        }

        if (datos.UltimaEjecucion is { } ultima && ultima > hoy)
        {
            error(Campo + "ultimaEjecucion", "El último preventivo no puede ser una fecha futura.");
            valido = false;
        }

        var proxima = datos.ProximaFecha ?? (valido ? datos.UltimaEjecucion?.AddDays(datos.PeriodicidadDias) : null);
        if (proxima is null)
        {
            if (valido)
            {
                error(Campo + "proximaFecha", "Si no se conoce el último preventivo, indique la fecha del próximo.");
            }

            return null;
        }

        if (datos.UltimaEjecucion is null && proxima < hoy)
        {
            error(Campo + "proximaFecha", "El próximo preventivo no puede ser una fecha pasada.");
            return null;
        }

        if (datos.UltimaEjecucion is { } anterior && proxima <= anterior)
        {
            error(Campo + "proximaFecha", "El próximo preventivo debe ser posterior al último.");
            return null;
        }

        var descripcion = string.IsNullOrWhiteSpace(datos.Descripcion) ? DescripcionPreventivo : datos.Descripcion.Trim();
        if (descripcion.Length > 300)
        {
            error(Campo + "descripcion", "Máximo 300 caracteres.");
            return null;
        }

        return valido ? new PlanCalculado(datos.PeriodicidadDias, datos.UltimaEjecucion, proxima.Value, descripcion) : null;
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // Las validaciones del dominio (ArgumentException) se responden como 400 por campo.
    private static T Construir<T>(Func<T> accion)
    {
        try
        {
            return accion();
        }
        catch (ArgumentException error)
        {
            throw ErrorValidacion.DeCampo(error.ParamName ?? "datos", error.Message.Split(" (Parameter")[0]);
        }
    }
}
