using System.Globalization;
using Sitbio.Application.Analitica;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Indicadores;
using Sitbio.Application.Instituciones;

namespace Sitbio.Application.Reportes;

/// <summary>Gráfico que el cliente captura de la pantalla (PNG en base64) para ponerlo en el PDF.</summary>
public sealed record GraficoAdjunto(string? Titulo, string? Png);

/// <summary>Cuerpo de POST /reportes/indicadores.pdf: los filtros de la pantalla y sus gráficos.</summary>
public sealed record SolicitudPdfIndicadores(DateOnly? Desde, DateOnly? Hasta, int? Meses, IReadOnlyList<GraficoAdjunto>? Graficos);

/// <summary>Cuerpo de POST /reportes/analitica.pdf: número de grupos, ejes elegidos y gráficos.</summary>
public sealed record SolicitudPdfAnalitica(int? Grupos, string? EjeX, string? EjeY, IReadOnlyList<GraficoAdjunto>? Graficos);

/// <summary>Gráfico ya validado, listo para el PDF.</summary>
public sealed record ImagenGrafico(string Titulo, byte[] Png);

/// <summary>Reportes e indicadores en PDF (HU-026): indicadores del periodo, serie mensual, parque y gráficos.</summary>
public sealed record IndicadoresDocumento(
    EncabezadoReporte Encabezado,
    IndicadoresDto Indicadores,
    IReadOnlyList<MesIndicadoresDto> Serie,
    ParqueDto Parque,
    IReadOnlyList<ImagenGrafico> Graficos,
    IReadOnlyList<string> ComoLeerlo);

/// <summary>Fila de la tabla por equipo del PDF de Analítica, con los valores de los ejes elegidos.</summary>
public sealed record FilaEquipoPdf(string Placa, string Tipo, string Grupo, string ValorX, string ValorY);

/// <summary>Tarjeta de un grupo en el PDF de Analítica.</summary>
public sealed record FilaGrupoPdf(string Nombre, int Equipos, string Fallas, string CostoMedio, string Atencion);

/// <summary>Analítica en PDF (HU-009): grupos, equipos con los ejes elegidos, gráficos y «Cómo leerlo».</summary>
public sealed record AnaliticaDocumento(
    EncabezadoReporte Encabezado,
    string TituloEjeX,
    string TituloEjeY,
    IReadOnlyList<FilaGrupoPdf> Grupos,
    IReadOnlyList<FilaEquipoPdf> Equipos,
    IReadOnlyList<ImagenGrafico> Graficos,
    IReadOnlyList<string> ComoLeerlo);

/// <summary>Variable de un eje de la analítica; la misma lista que la pantalla (UI-023).</summary>
public sealed record EjeAnalitica(string Clave, string Nombre, string Unidad, Func<CaracteristicasDto, double?> Valor)
{
    public string Titulo => string.IsNullOrEmpty(Unidad) ? Nombre : $"{Nombre} ({Unidad})";
}

/// <summary>
/// Exportación a PDF de Reportes e indicadores (UI-021) y de Analítica (UI-023) con sus gráficos (J-03). El cliente
/// solo envía los filtros y las imágenes de los gráficos; los valores de las tablas se vuelven a consultar aquí, con
/// los datos de la institución del token (RN-008), para que el PDF no dependa de lo que diga el cliente. Cada
/// exportación queda en la bitácora (EXPORTAR). Los roles los exige la API: los mismos de cada pantalla.
/// </summary>
public sealed class ExportacionGraficos(
    ConsultaIndicadores indicadores,
    ConsultaAnalitica analitica,
    IRepositorioInstitucion instituciones,
    IGeneradorReportes generador,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const int MaximoGraficos = 6;

    /// <summary>Tope por imagen (PNG a 2x de un gráfico de pantalla completa pesa unos 300 KB).</summary>
    public const int MaximoBytesImagen = 2 * 1024 * 1024;

    public const int MaximoTitulo = 120;

    public const string EntidadIndicadores = "indicadores";
    public const string EntidadAnalitica = "resultados_analitica";

    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static IReadOnlyList<EjeAnalitica> Ejes { get; } =
    [
        new("fallas", "Fallas (correctivos)", string.Empty, c => c.Intervenciones is { } n && c.ProporcionCorrectivos is { } p ? n * p : null),
        new("costo", "Costo confirmado", "M COP", c => c.CostoConfirmado is { } v ? v / 1_000_000 : null),
        new("tiempo", "Tiempo de atención", "min", c => c.TiempoMedioMin),
        new("intervenciones", "Intervenciones", string.Empty, c => c.Intervenciones),
        new("correctivos", "Proporción de correctivos", "%", c => c.ProporcionCorrectivos is { } p ? p * 100 : null),
    ];

    public static IReadOnlyList<string> ComoLeerIndicadores { get; } =
    [
        "Cumplimiento preventivo: mantenimientos preventivos cerrados sobre los programados en el periodo; la meta es 100 %.",
        "MTTR: horas en promedio desde que se crea una orden correctiva hasta que se cierra; entre menos, mejor.",
        "MTBF: horas en promedio entre fallas por equipo activo; entre más, mejor.",
        "Costo por equipo: costo de las intervenciones del periodo dividido entre los equipos activos.",
        "Fuera de servicio: equipos activos que hoy no se pueden usar, sobre el total de equipos activos.",
    ];

    public async Task<ArchivoReporte> IndicadoresPdfAsync(SolicitudPdfIndicadores? solicitud, CancellationToken cancellationToken)
    {
        solicitud ??= new SolicitudPdfIndicadores(null, null, null, null);
        var meses = solicitud.Meses ?? 6;
        var errores = new Dictionary<string, string[]>();
        if (meses is < 1 or > ConsultaIndicadores.MaximoMeses)
        {
            errores["meses"] = [$"El número de meses va de 1 a {ConsultaIndicadores.MaximoMeses}."];
        }

        var graficos = Graficos(solicitud.Graficos, errores);
        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var datos = await indicadores.ConsultarAsync(solicitud.Desde, solicitud.Hasta, cancellationToken);
        var serie = await indicadores.SerieAsync(meses, cancellationToken);
        var parque = await indicadores.ParqueAsync(cancellationToken);
        var encabezado = await EncabezadoAsync(
            "Reportes e indicadores",
            $"Del {Fecha(datos.Desde)} al {Fecha(datos.Hasta)} · mantenimientos de los últimos {meses} meses",
            cancellationToken);
        var pdf = generador.IndicadoresPdf(new IndicadoresDocumento(encabezado, datos, serie, parque, graficos, ComoLeerIndicadores));
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Exportar,
            EntidadIndicadores,
            contexto.TenantId.ToString(),
            new Dictionary<string, string>
            {
                ["reporte"] = "indicadores",
                ["formato"] = FormatosReporte.Pdf,
                ["desde"] = Iso(datos.Desde),
                ["hasta"] = Iso(datos.Hasta),
                ["graficos"] = graficos.Count.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
        return new ArchivoReporte(pdf, FormatosReporte.TipoPdf, $"indicadores-{datos.Desde.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{datos.Hasta.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pdf");
    }

    public async Task<ArchivoReporte> AnaliticaPdfAsync(SolicitudPdfAnalitica? solicitud, CancellationToken cancellationToken)
    {
        solicitud ??= new SolicitudPdfAnalitica(null, null, null, null);
        var errores = new Dictionary<string, string[]>();
        var k = solicitud.Grupos ?? 3;
        if (k is < 2 or > 6)
        {
            errores["grupos"] = ["El número de grupos va de 2 a 6."];
        }

        var ejeX = Eje(solicitud.EjeX ?? Ejes[0].Clave, "ejeX", errores);
        var ejeY = Eje(solicitud.EjeY ?? Ejes[1].Clave, "ejeY", errores);
        var graficos = Graficos(solicitud.Graficos, errores);
        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var resultado = await analitica.AgrupamientosAsync(cancellationToken);
        var agrupacion = resultado.Agrupaciones.FirstOrDefault(a => a.K == k)
            ?? throw ErrorValidacion.DeCampo("grupos", $"El último cálculo no incluye {k} grupos. Recalcula la analítica o elige otro número de grupos.");

        var nombres = agrupacion.Grupos.ToDictionary(g => g.Grupo, NombreGrupo);
        var grupos = agrupacion.Grupos
            .OrderByDescending(g => g.Grupo)
            .Select(g => new FilaGrupoPdf(
                NombreGrupo(g),
                g.Equipos,
                Numero(Ejes[0].Valor(g.Promedio)),
                g.Promedio.CostoConfirmado is { } c ? ((decimal)c).ToString("C0", Cultura) : "—",
                g.Promedio.TiempoMedioMin is { } t ? $"{t.ToString("0", Cultura)} min" : "—"))
            .ToList();
        var equipos = agrupacion.Equipos
            .OrderByDescending(e => e.Grupo)
            .ThenBy(e => e.PlacaInventario, StringComparer.Ordinal)
            .Select(e => new FilaEquipoPdf(
                e.PlacaInventario ?? "—",
                string.IsNullOrWhiteSpace(e.TipoEquipo) ? "—" : e.TipoEquipo,
                nombres.GetValueOrDefault(e.Grupo, $"Grupo {e.Grupo + 1}"),
                Numero(ejeX.Valor(e.Caracteristicas)),
                Numero(ejeY.Valor(e.Caracteristicas))))
            .ToList();

        var critico = agrupacion.Grupos.MaxBy(g => g.Grupo);
        var comoLeerlo = new List<string>
        {
            "Los equipos se agrupan con K-Means según sus fallas, costo, tiempo de atención e intervenciones; los grupos se ordenan por número de fallas.",
            $"Cada punto del gráfico es un equipo: el eje horizontal es «{ejeX.Titulo}» y el vertical, «{ejeY.Titulo}».",
        };
        if (critico is not null)
        {
            comoLeerlo.Add($"«{NombreGrupo(critico)}» reúne los equipos con más fallas: son candidatos para revisar con el comité de reposición.");
        }

        if (agrupacion.Inercia is { } inercia)
        {
            comoLeerlo.Add($"Inercia del modelo: {inercia.ToString("0.##", Cultura)} (más baja, grupos más compactos).");
        }

        comoLeerlo.Add("Los resultados son del último cálculo, no de este momento.");

        var encabezado = await EncabezadoAsync(
            "Analítica de equipos",
            $"{k} grupos · {agrupacion.Equipos.Count} equipos y {resultado.IntervencionesAnalizadas} intervenciones · calculado el {resultado.GeneradoEn.ToOffset(FechaColombia.Desfase).ToString("dd/MM/yyyy HH:mm", Cultura)}",
            cancellationToken);
        var pdf = generador.AnaliticaPdf(new AnaliticaDocumento(encabezado, ejeX.Titulo, ejeY.Titulo, grupos, equipos, graficos, comoLeerlo));
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Exportar,
            EntidadAnalitica,
            contexto.TenantId.ToString(),
            new Dictionary<string, string>
            {
                ["reporte"] = "analitica",
                ["formato"] = FormatosReporte.Pdf,
                ["grupos"] = k.ToString(CultureInfo.InvariantCulture),
                ["ejes"] = $"{ejeX.Clave}/{ejeY.Clave}",
                ["graficos"] = graficos.Count.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
        return new ArchivoReporte(pdf, FormatosReporte.TipoPdf, $"analitica-{k}-grupos.pdf");
    }

    /// <summary>Valida las imágenes: a lo sumo <see cref="MaximoGraficos"/>, PNG de verdad y de hasta 2 MB cada una.</summary>
    public static IReadOnlyList<ImagenGrafico> Graficos(IReadOnlyList<GraficoAdjunto>? adjuntos, IDictionary<string, string[]> errores)
    {
        if (adjuntos is null or { Count: 0 })
        {
            return [];
        }

        if (adjuntos.Count > MaximoGraficos)
        {
            errores["graficos"] = [$"Se pueden enviar hasta {MaximoGraficos} gráficos."];
            return [];
        }

        var imagenes = new List<ImagenGrafico>(adjuntos.Count);
        for (var i = 0; i < adjuntos.Count; i++)
        {
            var campo = $"graficos[{i}]";
            var png = adjuntos[i]?.Png;
            if (string.IsNullOrWhiteSpace(png) || png.Length > ((MaximoBytesImagen + 2) / 3 * 4) + 4)
            {
                errores[campo] = [string.IsNullOrWhiteSpace(png) ? "Falta la imagen del gráfico." : "Cada imagen puede pesar hasta 2 MB."];
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(png);
            }
            catch (FormatException)
            {
                errores[campo] = ["La imagen no es válida."];
                continue;
            }

            if (bytes.Length > MaximoBytesImagen)
            {
                errores[campo] = ["Cada imagen puede pesar hasta 2 MB."];
                continue;
            }

            if (bytes.Length < FirmaPng.Length || !bytes.AsSpan(0, FirmaPng.Length).SequenceEqual(FirmaPng))
            {
                errores[campo] = ["Solo se aceptan imágenes PNG."];
                continue;
            }

            var titulo = adjuntos[i]!.Titulo?.Trim();
            titulo = string.IsNullOrEmpty(titulo) ? "Gráfico" : titulo.Length > MaximoTitulo ? titulo[..MaximoTitulo] : titulo;
            imagenes.Add(new ImagenGrafico(titulo, bytes));
        }

        return imagenes;
    }

    private static EjeAnalitica Eje(string clave, string campo, IDictionary<string, string[]> errores)
    {
        var eje = Ejes.FirstOrDefault(e => string.Equals(e.Clave, clave.Trim(), StringComparison.OrdinalIgnoreCase));
        if (eje is null)
        {
            errores[campo] = [$"El eje debe ser uno de: {string.Join(", ", Ejes.Select(e => e.Clave))}."];
            return Ejes[0];
        }

        return eje;
    }

    private static string NombreGrupo(GrupoDto g) => string.IsNullOrWhiteSpace(g.Nombre) ? $"Grupo {g.Grupo + 1}" : g.Nombre;

    private static string Numero(double? valor) => valor is { } v ? v.ToString("0.##", Cultura) : "—";

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<EncabezadoReporte> EncabezadoAsync(string titulo, string subtitulo, CancellationToken cancellationToken)
    {
        var institucion = await instituciones.ObtenerAsync(cancellationToken);
        return new EncabezadoReporte(institucion?.Nombre ?? "SITBIO", institucion?.Nit ?? string.Empty, titulo, subtitulo, tiempo.GetUtcNow());
    }
}
