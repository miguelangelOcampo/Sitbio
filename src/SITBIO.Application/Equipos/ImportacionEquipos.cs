using System.Globalization;
using System.Text;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>Una hoja de la planilla: nombre y filas de texto (la primera es el encabezado).</summary>
public sealed record HojaPlanilla(string Nombre, IReadOnlyList<IReadOnlyList<string>> Filas);

/// <summary>Archivo generado para descargar.</summary>
public sealed record ArchivoGenerado(byte[] Contenido, string Nombre, string TipoMime);

/// <summary>Tipos de equipo, ubicaciones y proveedores de la institución, para buscar por nombre.</summary>
public sealed record CatalogosEquipo(
    IReadOnlyList<ElementoCatalogo> Tipos,
    IReadOnlyList<ElementoCatalogo> Ubicaciones,
    IReadOnlyList<ElementoCatalogo> Proveedores);

/// <summary>Lectura y escritura de CSV y Excel (.xlsx) para la importación masiva (HU-016).</summary>
public interface IPlanillaImportacion
{
    /// <summary>Filas de la primera hoja como texto (fechas en <c>yyyy-MM-dd</c>); <see cref="ErrorValidacion"/> si no se puede leer.</summary>
    IReadOnlyList<IReadOnlyList<string>> Leer(Stream contenido, string formato);

    /// <summary>CSV (solo la primera hoja, separado por punto y coma, UTF-8) o Excel con todas las hojas.</summary>
    byte[] Escribir(IReadOnlyList<HojaPlanilla> hojas, string formato);
}

/// <summary>Un problema de una fila del archivo (la fila 1 es el encabezado).</summary>
public sealed record ErrorFilaImportacion(int Fila, string? Placa, string Columna, string Mensaje);

/// <summary>
/// Resultado de procesar el archivo: en la vista previa (<see cref="Confirmada"/> = false) nada se guarda;
/// al confirmar se registran las filas válidas que caben en el plan (RN-009).
/// </summary>
public sealed record ResultadoImportacion(
    bool Confirmada,
    int TotalFilas,
    int FilasValidas,
    int CupoDisponible,
    int Importables,
    int Importadas,
    IReadOnlyList<ErrorFilaImportacion> Errores);

/// <summary>Columnas de la plantilla: hoja de vida (HU-002) e información regulatoria (RN-010).</summary>
public static class ColumnasImportacion
{
    public const string TipoEquipo = "Tipo de equipo";
    public const string Ubicacion = "Ubicación";
    public const string NumeroSerie = "Número de serie";
    public const string Placa = "Placa de inventario";
    public const string Marca = "Marca";
    public const string Modelo = "Modelo";
    public const string Proveedor = "Proveedor";
    public const string FechaAdquisicion = "Fecha de adquisición";
    public const string TipoAutorizacion = "Tipo de autorización";
    public const string RegistroInvima = "Registro INVIMA";
    public const string VenceRegistro = "Vence registro";
    public const string ClaseRiesgo = "Clase de riesgo";
    public const string RequiereCalibracion = "Requiere calibración";
    public const string Periodicidad = "Periodicidad de calibración (meses)";

    public const string RegistroSanitario = "Registro sanitario";
    public const string PermisoComercializacion = "Permiso de comercialización";

    public static IReadOnlyList<string> Todas { get; } =
    [
        TipoEquipo, Ubicacion, NumeroSerie, Placa, Marca, Modelo, Proveedor, FechaAdquisicion,
        TipoAutorizacion, RegistroInvima, VenceRegistro, ClaseRiesgo, RequiereCalibracion, Periodicidad,
    ];

    /// <summary>Las que no pueden faltar en el encabezado (proveedor, fecha de adquisición y periodicidad son opcionales).</summary>
    public static IReadOnlyList<string> Obligatorias { get; } =
        Todas.Where(c => c is not Proveedor and not FechaAdquisicion and not Periodicidad).ToList();

    /// <summary>Campo de <see cref="DatosEquipo"/> que valida <see cref="GestionEquipos"/> → columna de la plantilla.</summary>
    internal static IReadOnlyDictionary<string, string> PorCampo { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tipoEquipoId"] = TipoEquipo,
        ["ubicacionId"] = Ubicacion,
        ["proveedorId"] = Proveedor,
        ["numeroSerie"] = NumeroSerie,
        ["placaInventario"] = Placa,
        ["marca"] = Marca,
        ["modelo"] = Modelo,
        ["fechaAdquisicion"] = FechaAdquisicion,
        ["tipoAutorizacion"] = TipoAutorizacion,
        ["registroInvima"] = RegistroInvima,
        ["venceRegistro"] = VenceRegistro,
        ["claseRiesgo"] = ClaseRiesgo,
        ["requiereCalibracion"] = RequiereCalibracion,
        ["periodicidadCalibracionMeses"] = Periodicidad,
    };

    /// <summary>Sin tildes, espacios extra ni mayúsculas, para comparar encabezados y nombres de catálogo.</summary>
    public static string Normalizar(string? texto)
    {
        var descompuesto = (texto ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var limpio = new StringBuilder(descompuesto.Length);
        var espacio = false;
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                espacio = limpio.Length > 0;
                continue;
            }

            if (espacio)
            {
                limpio.Append(' ');
                espacio = false;
            }

            limpio.Append(char.ToLowerInvariant(c));
        }

        return limpio.ToString();
    }
}

/// <summary>
/// API-EQ-IMP (DOC-06; HU-016, RF-016): carga masiva de equipos desde CSV o Excel. Los permisos
/// (AC-01 y AC-02) los exige la API. Cada fila se valida como el formulario de HU-002 (obligatorios,
/// catálogos existentes de la institución, RN-010), más RN-001 contra la base y dentro del archivo;
/// solo se registran las filas válidas y sin pasar el máximo de equipos activos del plan (RN-009).
/// Al confirmar queda un evento IMPORTAR_CSV en la bitácora con los conteos (sin datos de las filas).
/// </summary>
public sealed class ImportacionEquipos(
    IRepositorioEquipos repositorio,
    GestionEquipos gestion,
    IPlanillaImportacion planilla,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto)
{
    public const string Entidad = GestionEquipos.Entidad;
    public const int MaximoFilas = 500;
    public const long TamanoMaximoBytes = 5L * 1024 * 1024;
    public const string FormatoCsv = "csv";
    public const string FormatoExcel = "xlsx";
    public const string FormatoNoPermitido = "Solo se aceptan archivos CSV o Excel (.xlsx).";
    public const string ColumnaPlan = "Plan";

    // 1900-01-01 a 2199-12-31 como número de serie de Excel.
    private const int SerieExcelMinima = 2;
    private const int SerieExcelMaxima = 109574;

    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"];

    /// <summary>Plantilla con el encabezado; en Excel, una segunda hoja con los valores permitidos de la institución.</summary>
    public async Task<ArchivoGenerado> PlantillaAsync(string? formato, CancellationToken cancellationToken)
    {
        var elegido = Formato(formato ?? FormatoExcel) ?? throw ErrorValidacion.DeCampo("formato", "El formato debe ser csv o xlsx.");
        var hojas = new List<HojaPlanilla> { new("Equipos", [ColumnasImportacion.Todas]) };
        if (elegido == FormatoExcel)
        {
            hojas.Add(ValoresPermitidos(await repositorio.CatalogosAsync(cancellationToken)));
        }

        var tipoMime = elegido == FormatoExcel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv";
        return new ArchivoGenerado(planilla.Escribir(hojas, elegido), $"plantilla-importacion-equipos.{elegido}", tipoMime);
    }

    /// <summary>Valida el archivo fila por fila y, si <paramref name="confirmar"/>, registra las filas válidas que caben en el plan.</summary>
    public async Task<ResultadoImportacion> ProcesarAsync(
        Stream contenido, string? nombreArchivo, long tamanoBytes, bool confirmar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var formato = Formato(Path.GetExtension(nombreArchivo ?? string.Empty).TrimStart('.'));
        if (tamanoBytes <= 0)
        {
            throw ErrorValidacion.DeCampo("archivo", "Selecciona un archivo.");
        }

        if (formato is null)
        {
            throw ErrorValidacion.DeCampo("archivo", FormatoNoPermitido);
        }

        if (tamanoBytes > TamanoMaximoBytes)
        {
            throw ErrorValidacion.DeCampo("archivo", "El archivo supera 5 MB.");
        }

        var filas = planilla.Leer(contenido, formato);
        var columnas = Encabezado(filas);
        var datos = filas.Skip(1)
            .Select((celdas, i) => (Numero: i + 2, Celdas: celdas))
            .Where(f => f.Celdas.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();
        if (datos.Count == 0)
        {
            throw ErrorValidacion.DeCampo("archivo", "El archivo no tiene filas de equipos.");
        }

        if (datos.Count > MaximoFilas)
        {
            throw ErrorValidacion.DeCampo("archivo", $"El archivo tiene {datos.Count} filas; se importan hasta {MaximoFilas} por archivo.");
        }

        var catalogos = await repositorio.CatalogosAsync(cancellationToken);
        var errores = new List<ErrorFilaImportacion>();
        var validas = new List<(int Fila, GestionEquipos.Validados Datos, DateOnly? Adquisicion)>();
        var series = new HashSet<string>(StringComparer.Ordinal);
        var placas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (numero, celdas) in datos)
        {
            string Celda(string columna) =>
                columnas.TryGetValue(columna, out var indice) && indice < celdas.Count ? celdas[indice].Trim() : string.Empty;

            var placa = Celda(ColumnasImportacion.Placa);
            var erroresFila = new List<ErrorFilaImportacion>();
            void Error(string columna, string mensaje) =>
                erroresFila.Add(new ErrorFilaImportacion(numero, placa.Length > 0 ? placa : null, columna, mensaje));

            var fila = Convertir(Celda, catalogos, Error);
            var validados = await Validar(fila, erroresFila, Error, cancellationToken);
            if (validados is not null)
            {
                // RN-001: contra la institución y contra las filas anteriores del mismo archivo.
                var duplicados = await repositorio.DuplicadosAsync(validados.Serie, validados.Placa, null, cancellationToken);
                if (duplicados.Placa)
                {
                    Error(ColumnasImportacion.Placa, $"Ya existe un equipo con la placa de inventario {validados.Placa} en tu institución.");
                }
                else if (placas.Contains(validados.Placa))
                {
                    Error(ColumnasImportacion.Placa, $"La placa de inventario {validados.Placa} se repite en el archivo.");
                }

                if (duplicados.Serie)
                {
                    Error(ColumnasImportacion.NumeroSerie, $"Ya existe un equipo con el número de serie {validados.Serie} en tu institución.");
                }
                else if (series.Contains(validados.Serie))
                {
                    Error(ColumnasImportacion.NumeroSerie, $"El número de serie {validados.Serie} se repite en el archivo.");
                }
            }

            if (validados is not null && erroresFila.Count == 0)
            {
                series.Add(validados.Serie);
                placas.Add(validados.Placa);
                validas.Add((numero, validados, fila.FechaAdquisicion));
            }

            errores.AddRange(erroresFila);
        }

        // RN-009 (CA-HU-016-03): se informa cuántos caben y no se pasa del máximo de equipos activos.
        var capacidad = await repositorio.CapacidadEquiposAsync(cancellationToken);
        var cupo = Math.Max(0, capacidad.Maximo - capacidad.Activos);
        var importables = Math.Min(cupo, validas.Count);
        foreach (var (fila, v, _) in validas.Skip(importables))
        {
            errores.Add(new ErrorFilaImportacion(
                fila,
                v.Placa,
                ColumnaPlan,
                $"No se importa: supera el máximo de equipos activos del plan {capacidad.Plan} ({capacidad.Activos} de {capacidad.Maximo}; caben {cupo})."));
        }

        var importadas = 0;
        if (confirmar)
        {
            foreach (var (_, v, adquisicion) in validas.Take(importables))
            {
                gestion.AgregarNuevo(v, adquisicion);
            }

            if (importables > 0)
            {
                await repositorio.GuardarAsync(cancellationToken);
                importadas = importables;
            }

            var detalle = new Dictionary<string, string>
            {
                ["formato"] = formato,
                ["filas"] = Numero(datos.Count),
                ["importadas"] = Numero(importadas),
                ["con_errores"] = Numero(datos.Count - validas.Count),
                ["omitidas_por_plan"] = Numero(validas.Count - importables),
            };
            await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.ImportarCsv, Entidad, contexto.TenantId.ToString(), detalle, cancellationToken);
        }

        return new ResultadoImportacion(confirmar, datos.Count, validas.Count, cupo, importables, importadas, errores.OrderBy(e => e.Fila).ToList());
    }

    private static string Numero(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string? Formato(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        FormatoCsv => FormatoCsv,
        FormatoExcel => FormatoExcel,
        _ => null,
    };

    /// <summary>Reglas del formulario de HU-002; sus errores pasan a la columna de la plantilla.</summary>
    private async Task<GestionEquipos.Validados?> Validar(
        DatosEquipo fila, List<ErrorFilaImportacion> erroresFila, Action<string, string> error, CancellationToken cancellationToken)
    {
        try
        {
            var validados = await gestion.ValidarNuevoAsync(fila, cancellationToken);
            return erroresFila.Count == 0 ? validados : null;
        }
        catch (ErrorValidacion invalido)
        {
            foreach (var (campo, mensajes) in invalido.Errores)
            {
                var columna = ColumnasImportacion.PorCampo.GetValueOrDefault(campo, campo);

                // Si la celda ya tiene un error de lectura, no se repite como «obligatorio».
                if (erroresFila.All(e => e.Columna != columna))
                {
                    foreach (var mensaje in mensajes)
                    {
                        error(columna, mensaje);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>Posición de cada columna conocida; si faltan obligatorias, 400 con sus nombres.</summary>
    private static Dictionary<string, int> Encabezado(IReadOnlyList<IReadOnlyList<string>> filas)
    {
        var conocidas = ColumnasImportacion.Todas.ToDictionary(ColumnasImportacion.Normalizar, c => c);
        var posiciones = new Dictionary<string, int>();
        var encabezado = filas.Count > 0 ? filas[0] : [];
        for (var i = 0; i < encabezado.Count; i++)
        {
            if (conocidas.TryGetValue(ColumnasImportacion.Normalizar(encabezado[i]), out var columna))
            {
                posiciones.TryAdd(columna, i);
            }
        }

        var faltan = ColumnasImportacion.Obligatorias.Where(c => !posiciones.ContainsKey(c)).ToList();
        if (faltan.Count > 0)
        {
            throw ErrorValidacion.DeCampo("archivo", $"Faltan columnas de la plantilla: {string.Join(", ", faltan)}. Descarga la plantilla y vuelve a intentarlo.");
        }

        return posiciones;
    }

    /// <summary>Texto de la fila → datos del formulario; los nombres de catálogo se buscan sin tildes ni mayúsculas.</summary>
    private static DatosEquipo Convertir(Func<string, string> celda, CatalogosEquipo catalogos, Action<string, string> error)
    {
        Guid? Buscar(string columna, IReadOnlyList<ElementoCatalogo> catalogo, string que)
        {
            var nombre = celda(columna);
            if (nombre.Length == 0)
            {
                return null;
            }

            var clave = ColumnasImportacion.Normalizar(nombre);
            var encontrado = catalogo
                .Where(e => ColumnasImportacion.Normalizar(e.Nombre) == clave)
                .OrderByDescending(e => e.Activo)
                .FirstOrDefault();
            if (encontrado is null)
            {
                error(columna, $"{que} «{nombre}» no existe en tu institución.");
                return null;
            }

            if (!encontrado.Activo)
            {
                error(columna, $"{que} «{nombre}» está inactivo.");
                return null;
            }

            return encontrado.Id;
        }

        DateOnly? Fecha(string columna)
        {
            var texto = celda(columna);
            if (texto.Length == 0)
            {
                return null;
            }

            if (DateOnly.TryParseExact(texto, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            {
                return fecha;
            }

            // Una fecha escrita en una columna de texto de Excel llega como su número de serie (días desde 1899-12-30).
            if (int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var serie) && serie is >= SerieExcelMinima and <= SerieExcelMaxima)
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serie));
            }

            error(columna, "Fecha no válida; usa AAAA-MM-DD o DD/MM/AAAA.");
            return null;
        }

        string? Autorizacion()
        {
            var clave = ColumnasImportacion.Normalizar(celda(ColumnasImportacion.TipoAutorizacion).Replace('_', ' '));
            switch (clave)
            {
                case "":
                    return null;
                case "registro sanitario":
                    return TiposAutorizacion.RegistroSanitario;
                case "permiso de comercializacion":
                case "permiso comercializacion":
                    return TiposAutorizacion.PermisoComercializacion;
                default:
                    error(ColumnasImportacion.TipoAutorizacion, $"Usa «{ColumnasImportacion.RegistroSanitario}» o «{ColumnasImportacion.PermisoComercializacion}».");
                    return null;
            }
        }

        string? Clase()
        {
            var texto = celda(ColumnasImportacion.ClaseRiesgo);
            return ClasesRiesgo.Todas.FirstOrDefault(c => string.Equals(c, texto, StringComparison.OrdinalIgnoreCase)) ?? (texto.Length == 0 ? null : texto);
        }

        bool? Calibracion()
        {
            switch (ColumnasImportacion.Normalizar(celda(ColumnasImportacion.RequiereCalibracion)))
            {
                case "":
                    return null;
                case "si" or "s" or "true" or "1" or "x":
                    return true;
                case "no" or "n" or "false" or "0":
                    return false;
                default:
                    error(ColumnasImportacion.RequiereCalibracion, "Escribe «Sí» o «No».");
                    return null;
            }
        }

        int? Periodicidad()
        {
            var texto = celda(ColumnasImportacion.Periodicidad);
            if (texto.Length == 0)
            {
                return null;
            }

            if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
                && valor == decimal.Truncate(valor) && valor is >= int.MinValue and <= int.MaxValue)
            {
                return (int)valor;
            }

            error(ColumnasImportacion.Periodicidad, "Escribe la periodicidad en meses, como número entero.");
            return null;
        }

        return new DatosEquipo(
            Buscar(ColumnasImportacion.TipoEquipo, catalogos.Tipos, "El tipo de equipo"),
            Buscar(ColumnasImportacion.Ubicacion, catalogos.Ubicaciones, "La ubicación"),
            Buscar(ColumnasImportacion.Proveedor, catalogos.Proveedores, "El proveedor"),
            celda(ColumnasImportacion.NumeroSerie),
            celda(ColumnasImportacion.Placa),
            celda(ColumnasImportacion.Marca),
            celda(ColumnasImportacion.Modelo),
            Fecha(ColumnasImportacion.FechaAdquisicion),
            Autorizacion(),
            celda(ColumnasImportacion.RegistroInvima),
            Fecha(ColumnasImportacion.VenceRegistro),
            Clase(),
            Calibracion(),
            Periodicidad());
    }

    private static HojaPlanilla ValoresPermitidos(CatalogosEquipo catalogos)
    {
        var columnas = new List<(string Titulo, IReadOnlyList<string> Valores)>
        {
            (ColumnasImportacion.TipoEquipo, catalogos.Tipos.Where(t => t.Activo).Select(t => t.Nombre).ToList()),
            (ColumnasImportacion.Ubicacion, catalogos.Ubicaciones.Where(u => u.Activo).Select(u => u.Nombre).ToList()),
            (ColumnasImportacion.Proveedor, catalogos.Proveedores.Where(p => p.Activo).Select(p => p.Nombre).ToList()),
            (ColumnasImportacion.TipoAutorizacion, [ColumnasImportacion.RegistroSanitario, ColumnasImportacion.PermisoComercializacion]),
            (ColumnasImportacion.ClaseRiesgo, ClasesRiesgo.Todas),
            (ColumnasImportacion.RequiereCalibracion, ["Sí", "No"]),
            ("Fechas", ["AAAA-MM-DD o DD/MM/AAAA"]),
        };
        var alto = columnas.Max(c => c.Valores.Count);
        var filas = new List<IReadOnlyList<string>> { columnas.Select(c => c.Titulo).ToList() };
        for (var i = 0; i < alto; i++)
        {
            filas.Add(columnas.Select(c => i < c.Valores.Count ? c.Valores[i] : string.Empty).ToList());
        }

        return new HojaPlanilla("Valores permitidos", filas);
    }
}
