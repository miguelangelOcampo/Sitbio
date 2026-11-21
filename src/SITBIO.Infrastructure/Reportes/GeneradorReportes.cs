using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sitbio.Application.Reportes;
using Sitbio.Domain.Mantenimiento;

namespace Sitbio.Infrastructure.Reportes;

/// <summary>
/// Reportes en el servidor (ADR-009): PDF con QuestPDF (licencia Community, RT-008) y Excel con ClosedXML. Las
/// fechas van en hora de Colombia.
/// </summary>
public sealed class GeneradorReportes : IGeneradorReportes
{
    private const string Primario = "#1F4E79";
    private const string FormatoFechaExcel = "dd/mm/yyyy hh:mm";
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    static GeneradorReportes() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] HistorialPdf(EncabezadoReporte encabezado, IReadOnlyList<FilaHistorialOrden> filas) =>
        Document.Create(doc => doc.Page(pagina =>
        {
            Pagina(pagina, encabezado, PageSizes.A4.Landscape());
            pagina.Content().PaddingTop(10).Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(62);
                    c.ConstantColumn(58);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1.5f);
                    c.ConstantColumn(58);
                    c.ConstantColumn(62);
                    c.RelativeColumn(1.5f);
                    c.ConstantColumn(62);
                    c.ConstantColumn(62);
                    c.RelativeColumn(1.5f);
                });
                tabla.Header(h =>
                {
                    foreach (var columna in ColumnasHistorial.Nombres)
                    {
                        h.Cell().Background(Primario).Padding(4).Text(columna).FontColor(Colors.White).SemiBold();
                    }
                });
                var par = false;
                foreach (var f in filas)
                {
                    var fondo = par ? Colors.Grey.Lighten4 : Colors.White;
                    par = !par;
                    foreach (var valor in Valores(f))
                    {
                        tabla.Cell().Background(fondo).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(valor);
                    }
                }
            });
        })).GeneratePdf();

    public byte[] HistorialExcel(EncabezadoReporte encabezado, IReadOnlyList<FilaHistorialOrden> filas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Historial");
        hoja.Cell(1, 1).Value = encabezado.Titulo;
        hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        hoja.Cell(2, 1).Value = string.IsNullOrEmpty(encabezado.Nit) ? encabezado.Institucion : $"{encabezado.Institucion} · NIT {encabezado.Nit}";
        hoja.Cell(3, 1).Value = $"{encabezado.Subtitulo} · generado el {Fecha(encabezado.GeneradoEn)}";

        const int filaTitulos = 5;
        var columnas = ColumnasHistorial.Nombres.Count;
        for (var c = 0; c < columnas; c++)
        {
            hoja.Cell(filaTitulos, c + 1).Value = ColumnasHistorial.Nombres[c];
        }

        hoja.Range(filaTitulos, 1, filaTitulos, columnas).Style
            .Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml(Primario));

        var fila = filaTitulos + 1;
        foreach (var f in filas)
        {
            var valores = Valores(f);
            for (var c = 0; c < valores.Count; c++)
            {
                hoja.Cell(fila, c + 1).Value = valores[c];
            }

            hoja.Cell(fila, 1).Value = f.FechaCreacion.ToOffset(Colombia).DateTime;
            hoja.Cell(fila, 1).Style.DateFormat.Format = FormatoFechaExcel;
            if (f.FechaCierre is { } cierre)
            {
                hoja.Cell(fila, 9).Value = cierre.ToOffset(Colombia).DateTime;
                hoja.Cell(fila, 9).Style.DateFormat.Format = FormatoFechaExcel;
            }

            fila++;
        }

        hoja.Range(filaTitulos, 1, Math.Max(filaTitulos, fila - 1), columnas).SetAutoFilter();
        hoja.SheetView.FreezeRows(filaTitulos);
        hoja.Columns(1, columnas).AdjustToContents(filaTitulos, Math.Min(fila, filaTitulos + 200));

        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }

    public byte[] HojaVidaPdf(HojaVidaDocumento documento) =>
        Document.Create(doc => doc.Page(pagina =>
        {
            Pagina(pagina, documento.Encabezado, PageSizes.A4);
            pagina.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text("Datos técnicos").FontSize(12).SemiBold().FontColor(Primario);
                var foto = Imagen(documento.Foto);
                col.Item().Row(fila =>
                {
                    if (foto is not null)
                    {
                        // La foto va a la izquierda de los datos técnicos, en un recuadro de 150 pt.
                        fila.ConstantItem(150).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Height(150).AlignCenter().AlignMiddle().Image(foto).FitArea();
                        fila.ConstantItem(12);
                    }

                    fila.RelativeItem().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(foto is null ? 150 : 120);
                            c.RelativeColumn();
                        });
                        foreach (var (etiqueta, valor) in DatosTecnicos(documento))
                        {
                            tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(etiqueta).FontColor(Colors.Grey.Darken2);
                            tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(valor ?? "—");
                        }
                    });
                });

                col.Item().PaddingTop(12).Text("Historial").FontSize(12).SemiBold().FontColor(Primario);
                if (!documento.Historial.IntervencionesCompletas)
                {
                    col.Item().Background(Colors.Orange.Lighten4).Padding(6)
                        .Text("No se pudieron leer las intervenciones del repositorio de documentos; el resto del historial está completo.");
                }

                col.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(85);
                        c.RelativeColumn();
                        c.ConstantColumn(110);
                    });
                    tabla.Header(h =>
                    {
                        foreach (var titulo in new[] { "Fecha", "Evento", "Responsable" })
                        {
                            h.Cell().Background(Primario).Padding(4).Text(titulo).FontColor(Colors.White).SemiBold();
                        }
                    });
                    foreach (var evento in documento.Historial.Eventos)
                    {
                        tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Fecha(evento.Fecha));
                        tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Column(c =>
                        {
                            c.Item().Text(evento.Titulo).SemiBold();
                            if (evento.Detalle is { } detalle)
                            {
                                c.Item().Text(detalle).FontColor(Colors.Grey.Darken2);
                            }
                        });
                        tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(evento.Responsable ?? "—");
                    }
                });
            });
        })).GeneratePdf();

    public byte[] IndicadoresPdf(IndicadoresDocumento documento) =>
        Document.Create(doc => doc.Page(pagina =>
        {
            Pagina(pagina, documento.Encabezado, PageSizes.A4);
            pagina.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text("Indicadores del periodo").FontSize(12).SemiBold().FontColor(Primario);
                Tabla(col, ["Indicador", "Valor", "Detalle"], [150, 0, 0], FilasIndicadores(documento.Indicadores));
                if (!documento.Indicadores.CostosCompletos)
                {
                    col.Item().Background(Colors.Orange.Lighten4).Padding(6)
                        .Text("No se pudo leer el costo de las intervenciones; el costo por equipo no está disponible.");
                }

                Graficos(col, documento.Graficos);

                col.Item().PaddingTop(8).Text("Mantenimientos por mes").FontSize(12).SemiBold().FontColor(Primario);
                Tabla(
                    col,
                    ["Mes", "Preventivos", "Correctivos", "Total", "Cumplimiento", "MTTR"],
                    [0, 0, 0, 0, 0, 0],
                    documento.Serie.Select(m => (IReadOnlyList<string>)
                    [
                        m.Mes.ToString("MMMM yyyy", Cultura),
                        m.Preventivos.ToString(Cultura),
                        m.Correctivos.ToString(Cultura),
                        (m.Preventivos + m.Correctivos).ToString(Cultura),
                        m.CumplimientoPreventivo is { } c ? $"{c.ToString("0.#", Cultura)} %" : "—",
                        m.MttrHoras is { } h ? $"{h.ToString("0.#", Cultura)} h" : "—",
                    ]).ToList());

                var p = documento.Parque;
                col.Item().PaddingTop(8).Text("Estado del parque").FontSize(12).SemiBold().FontColor(Primario);
                Tabla(col, ["Operativos", "En mantenimiento", "Fuera de servicio"], [0, 0, 0],
                    [[p.Operativos.ToString(Cultura), p.EnMantenimiento.ToString(Cultura), p.FueraDeServicio.ToString(Cultura)]]);
                if (p.EquiposFuera.Count > 0)
                {
                    col.Item().PaddingTop(4).Text("Equipos fuera de servicio").SemiBold();
                    Tabla(col, ["Placa", "Equipo", "Ubicación"], [80, 0, 0],
                        p.EquiposFuera.Select(e => (IReadOnlyList<string>)[e.PlacaInventario, e.TipoEquipo, e.Ubicacion]).ToList());
                }

                ComoLeerlo(col, documento.ComoLeerlo);
            });
        })).GeneratePdf();

    public byte[] AnaliticaPdf(AnaliticaDocumento documento) =>
        Document.Create(doc => doc.Page(pagina =>
        {
            Pagina(pagina, documento.Encabezado, PageSizes.A4);
            pagina.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(6);
                Graficos(col, documento.Graficos);
                col.Item().PaddingTop(4).Text("Grupos").FontSize(12).SemiBold().FontColor(Primario);
                Tabla(col, ["Grupo", "Equipos", "Fallas", "Costo medio", "Atención"], [0, 50, 55, 80, 60],
                    documento.Grupos.Select(g => (IReadOnlyList<string>)[g.Nombre, g.Equipos.ToString(Cultura), g.Fallas, g.CostoMedio, g.Atencion]).ToList());
                ComoLeerlo(col, documento.ComoLeerlo);
                col.Item().PaddingTop(8).Text("Equipos").FontSize(12).SemiBold().FontColor(Primario);
                Tabla(col, ["Placa", "Tipo", "Grupo", documento.TituloEjeX, documento.TituloEjeY], [70, 0, 0, 85, 85],
                    documento.Equipos.Select(e => (IReadOnlyList<string>)[e.Placa, e.Tipo, e.Grupo, e.ValorX, e.ValorY]).ToList());
            });
        })).GeneratePdf();

    /// <summary>Filas de la tabla de indicadores del PDF: indicador, valor y detalle.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FilasIndicadores(Application.Indicadores.IndicadoresDto i) =>
    [
        ["Cumplimiento preventivo", Porcentaje(i.CumplimientoPreventivo.Porcentaje), $"{i.CumplimientoPreventivo.Cumplidas} de {i.CumplimientoPreventivo.Programadas} programadas"],
        ["MTTR", i.MttrHoras is { } r ? $"{r.ToString("0.#", Cultura)} h" : "—", "Tiempo medio de reparación"],
        ["MTBF", i.MtbfHoras is { } b ? $"{b.ToString("0.#", Cultura)} h" : "—", "Tiempo medio entre fallas"],
        ["Costo por equipo", i.CostoPorEquipo.Promedio is { } c ? c.ToString("C0", Cultura) : "—",
            i.CostoPorEquipo.CostoTotal is { } t ? $"{t.ToString("C0", Cultura)} entre {i.CostoPorEquipo.EquiposActivos} equipos activos" : "Sin costo registrado"],
        ["Fuera de servicio", Porcentaje(i.FueraDeServicio.Porcentaje), $"{i.FueraDeServicio.Equipos} de {i.FueraDeServicio.EquiposActivos} equipos activos"],
    ];

    private static string Porcentaje(double? valor) => valor is { } v ? $"{v.ToString("0.#", Cultura)} %" : "—";

    /// <summary>Cada gráfico con su título, a todo el ancho y sin partirse entre páginas.</summary>
    private static void Graficos(ColumnDescriptor col, IReadOnlyList<ImagenGrafico> graficos)
    {
        foreach (var grafico in graficos)
        {
            if (Imagen(grafico.Png) is not { } imagen)
            {
                continue;
            }

            col.Item().PaddingTop(8).ShowEntire().Column(c =>
            {
                c.Item().Text(grafico.Titulo).FontSize(11).SemiBold().FontColor(Primario);
                // A su tamaño en pantalla como máximo (el PNG viene a 2x, 192 ppp): un gráfico pequeño no se estira.
                var (ancho, alto) = TamanoEnPuntos(grafico.Png);
                c.Item().PaddingTop(4).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter()
                    .MaxWidth(ancho).MaxHeight(Math.Min(alto, 300)).Image(imagen).FitArea();
            });
        }
    }

    /// <summary>Tamaño en puntos de un PNG capturado a 192 ppp (ancho y alto de la cabecera IHDR); 1 píxel = 0,375 pt.</summary>
    public static (float Ancho, float Alto) TamanoEnPuntos(byte[] png)
    {
        const float PuntosPorPixel = 72f / 192f;
        if (png.Length < 24)
        {
            return (600, 300);
        }

        var ancho = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var alto = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return ancho <= 0 || alto <= 0 ? (600, 300) : (ancho * PuntosPorPixel, alto * PuntosPorPixel);
    }

    private static void ComoLeerlo(ColumnDescriptor col, IReadOnlyList<string> textos)
    {
        if (textos.Count == 0)
        {
            return;
        }

        col.Item().PaddingTop(8).ShowEntire().Background(Colors.Blue.Lighten5).Padding(8).Column(c =>
        {
            c.Spacing(3);
            c.Item().Text("Cómo leerlo").SemiBold().FontColor(Primario);
            foreach (var texto in textos)
            {
                c.Item().Text("• " + texto);
            }
        });
    }

    /// <summary>Tabla con encabezado de color; un ancho 0 es una columna relativa.</summary>
    private static void Tabla(ColumnDescriptor col, IReadOnlyList<string> titulos, IReadOnlyList<float> anchos, IReadOnlyList<IReadOnlyList<string>> filas) =>
        col.Item().Table(tabla =>
        {
            tabla.ColumnsDefinition(c =>
            {
                foreach (var ancho in anchos)
                {
                    if (ancho > 0)
                    {
                        c.ConstantColumn(ancho);
                    }
                    else
                    {
                        c.RelativeColumn();
                    }
                }
            });
            tabla.Header(h =>
            {
                foreach (var titulo in titulos)
                {
                    h.Cell().Background(Primario).Padding(4).Text(titulo).FontColor(Colors.White).SemiBold();
                }
            });
            var par = false;
            foreach (var fila in filas)
            {
                var fondo = par ? Colors.Grey.Lighten4 : Colors.White;
                foreach (var valor in fila)
                {
                    tabla.Cell().Background(fondo).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(valor);
                }

                par = !par;
            }
        });

    /// <summary>La fotografía lista para el PDF; null si no hay o si su contenido no es una imagen legible.</summary>
    public static Image? Imagen(byte[]? foto)
    {
        if (foto is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return Image.FromBinaryData(foto);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or QuestPDF.Drawing.Exceptions.DocumentComposeException)
        {
            return null;
        }
    }

    /// <summary>Etiqueta y valor de los datos técnicos de la hoja de vida (RF-030).</summary>
    public static IReadOnlyList<(string Etiqueta, string? Valor)> DatosTecnicos(HojaVidaDocumento documento)
    {
        var e = documento.Equipo;
        var vence = e.VenceRegistro is { } v ? $" · vence el {v.ToString("dd/MM/yyyy", Cultura)}" : string.Empty;
        return
        [
            ("Placa de inventario", e.PlacaInventario),
            ("Número de serie", e.NumeroSerie),
            ("Tipo de equipo", e.TipoEquipo),
            ("Marca y modelo", $"{e.Marca} {e.Modelo}"),
            ("Ubicación", e.Ubicacion),
            ("Estado", e.EstadoNombre),
            ("Clase de riesgo", e.ClaseRiesgo),
            ("Registro INVIMA", e.RegistroInvima is null ? null : e.RegistroInvima + vence),
            ("Requiere calibración", e.RequiereCalibracion ? $"Sí · cada {e.PeriodicidadCalibracionMeses} meses" : "No"),
            ("Proveedor", e.Proveedor),
            ("Fecha de adquisición", e.FechaAdquisicion?.ToString("dd/MM/yyyy", Cultura)),
            ("Próximo preventivo", e.Preventivo?.ProximaFecha.ToString("dd/MM/yyyy", Cultura)),
            ("Fecha de baja", e.FechaBaja is { } b ? Fecha(b) : null),
        ];
    }

    private static void Pagina(PageDescriptor pagina, EncabezadoReporte encabezado, PageSize tamano)
    {
        pagina.Size(tamano);
        pagina.Margin(28);
        pagina.DefaultTextStyle(t => t.FontSize(8.5f));
        var nit = string.IsNullOrEmpty(encabezado.Nit) ? string.Empty : $" · NIT {encabezado.Nit}";
        pagina.Header().Column(c =>
        {
            c.Item().Text(encabezado.Titulo).FontSize(16).SemiBold().FontColor(Primario);
            c.Item().Text(encabezado.Subtitulo).FontColor(Colors.Grey.Darken2);
            c.Item().Text($"{encabezado.Institucion}{nit} · generado el {Fecha(encabezado.GeneradoEn)}").FontColor(Colors.Grey.Darken1);
        });
        pagina.Footer().AlignRight().Text(t =>
        {
            t.Span("SITBIO · página ");
            t.CurrentPageNumber();
            t.Span(" de ");
            t.TotalPages();
        });
    }

    /// <summary>Los valores de una fila en el orden de <see cref="ColumnasHistorial.Nombres"/>.</summary>
    public static IReadOnlyList<string> Valores(FilaHistorialOrden f) =>
    [
        Fecha(f.FechaCreacion),
        f.PlacaInventario,
        f.TipoEquipo,
        f.Ubicacion,
        f.TipoMantenimiento,
        f.CodigoTiquete ?? "—",
        f.Tecnico,
        Estado(f.Estado),
        f.FechaCierre is { } c ? Fecha(c) : "—",
        f.AprobadoPor ?? "—",
    ];

    private static string Estado(string estado) => estado switch
    {
        EstadosOrden.Asignada => "Asignada",
        EstadosOrden.EnEjecucion => "En ejecución",
        EstadosOrden.Pausada => "Pausada",
        EstadosOrden.Cerrada => "Cerrada",
        EstadosOrden.Aprobada => "Aprobada",
        _ => estado,
    };

    private static string Fecha(DateTimeOffset fecha) => fecha.ToOffset(Colombia).ToString("dd/MM/yyyy HH:mm", Cultura);
}
