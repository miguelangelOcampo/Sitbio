using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-023 Analítica (HU-008, HU-009; prototipo Analitica, I-05): dispersión de los equipos con los ejes que elija el
/// usuario, coloreados por grupo K-Means con su nombre; el número de grupos (2 a 6) cambia al instante porque el lote
/// guarda todas las agrupaciones. El ingeniero biomédico recalcula desde aquí; un clic en un punto abre la hoja de
/// vida. Si se piden más grupos que equipos, pide reducirlos (CA-HU-009-02).
/// </summary>
public partial class AnaliticaViewModel(IServicioAnalitica servicio, SesionActual? sesion = null, IDialogoArchivos? dialogo = null)
    : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "UI-023";

    /// <summary>Consultas del estado mientras se recalcula (cada 2 s, hasta 5 minutos).</summary>
    public const int ConsultasMaximas = 150;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Colores de los grupos según cuántos hay: el último (más fallas) siempre en rojo.</summary>
    private static readonly IReadOnlyDictionary<int, string[]> Colores = new Dictionary<int, string[]>
    {
        [1] = ["Brush.Primary"],
        [2] = ["Brush.Primary", "Brush.Danger"],
        [3] = ["Brush.Primary", "Brush.Warning", "Brush.Danger"],
        [4] = ["Brush.Primary", "Brush.Info", "Brush.Warning", "Brush.Danger"],
        [5] = ["Brush.Primary", "Brush.Info", "Brush.Jade", "Brush.Warning", "Brush.Danger"],
        [6] = ["Brush.Primary", "Brush.Info", "Brush.Jade", "Brush.Text3", "Brush.Warning", "Brush.Danger"],
    };

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    public static IReadOnlyList<int> OpcionesGrupos { get; } = [2, 3, 4, 5, 6];

    public static IReadOnlyList<VariableAnalitica> OpcionesEje { get; } =
    [
        new("fallas", "Fallas (correctivos)", string.Empty, c => c.Intervenciones is { } n && c.ProporcionCorrectivos is { } p ? n * p : null),
        new("costo", "Costo confirmado", "M COP", c => c.CostoConfirmado is { } v ? v / 1_000_000 : null),
        new("tiempo", "Tiempo de atención", "min", c => c.TiempoMedioMin),
        new("intervenciones", "Intervenciones", string.Empty, c => c.Intervenciones),
        new("correctivos", "Proporción de correctivos", "%", c => c.ProporcionCorrectivos is { } p ? p * 100 : null),
    ];

    /// <summary>Pausa entre consultas del estado del recálculo; las pruebas la ponen en cero.</summary>
    public TimeSpan IntervaloConsulta { get; set; } = TimeSpan.FromSeconds(2);

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecalcularCommand))]
    private bool _recalculando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDatos), nameof(Encabezado))]
    [NotifyCanExecuteChangedFor(nameof(ExportarCommand))]
    private Agrupamiento? _resultado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportarPdfCommand))]
    private bool _exportandoPdf;

    /// <summary>La vista la asigna: captura el gráfico visible para el PDF (J-03).</summary>
    public Func<IReadOnlyList<GraficoCapturado>>? CapturarGraficos { get; set; }

    [ObservableProperty]
    private int _gruposSolicitados = 3;

    [ObservableProperty]
    private VariableAnalitica _ejeX = OpcionesEje[0];

    [ObservableProperty]
    private VariableAnalitica _ejeY = OpcionesEje[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaGrafico))]
    private bool _vistaTabla;

    /// <summary>«Vista: Gráfico / Tabla de datos», como en Reportes; la tabla es la alternativa accesible de la dispersión.</summary>
    public bool VistaGrafico
    {
        get => !VistaTabla;
        set => VistaTabla = !value;
    }

    public bool HayDatos => Resultado is { Equipos.Count: > 0 };

    /// <summary>Solo el ingeniero biomédico (AC-02) recalcula, como en el prototipo.</summary>
    public bool PuedeRecalcular => sesion?.Sesion?.Rol == "AC-02";

    /// <summary>Solo los roles con Equipos biomédicos en su menú abren la hoja de vida al hacer clic en un punto.</summary>
    public bool PuedeAbrirEquipos => sesion?.Sesion?.Rol is { } rol && MenuPorRol.Para(rol).SelectMany(m => m.Opciones).Any(o => o.Id == EquiposViewModel.IdPantalla);

    public string Encabezado => Resultado is { } r
        ? $"{r.Equipos.Count} equipos y {r.IntervencionesAnalizadas} intervenciones · calculado el {r.GeneradoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Cultura)}"
        : string.Empty;

    /// <summary>La agrupación con el número de grupos elegido, o null si el último cálculo no la tiene.</summary>
    public AgrupacionAnalitica? Agrupacion => Resultado is not { } r
        ? null
        : r.Agrupaciones?.FirstOrDefault(a => a.K == GruposSolicitados)
            ?? (r.K == GruposSolicitados ? new AgrupacionAnalitica(r.K, null, r.Grupos, r.Equipos) : null);

    public bool HayAgrupacion => Agrupacion is { Equipos.Count: > 0 };

    /// <summary>CA-HU-009-02: con menos equipos que grupos pide reducirlos; sin esa agrupación, ofrece recalcular.</summary>
    public Aviso? AvisoGrupos
    {
        get
        {
            if (Resultado is not { } r)
            {
                return null;
            }

            if (r.Equipos.Count < GruposSolicitados)
            {
                return new Aviso(
                    "Reduce el número de grupos",
                    $"Hay {r.Equipos.Count} equipos con intervenciones y pediste {GruposSolicitados} grupos. Elige {r.Equipos.Count} o menos.",
                    null);
            }

            return Agrupacion is not null
                ? null
                : new Aviso(
                    "Este número de grupos aún no está calculado",
                    PuedeRecalcular
                        ? $"El último cálculo no incluye {GruposSolicitados} grupos. Presiona «Recalcular» para calcularlos."
                        : $"El último cálculo no incluye {GruposSolicitados} grupos. Pide a un ingeniero biomédico que recalcule la analítica.",
                    null,
                    EsError: false);
        }
    }

    public string TituloEjeX => Titulo(EjeX);

    public string TituloEjeY => Titulo(EjeY);

    public string TituloGrafico => $"{EjeY.Nombre} frente a {EjeX.Nombre.ToLower(Cultura)}";

    /// <summary>Leyenda: una serie por grupo, con su nombre y color.</summary>
    public IReadOnlyList<SerieGrafico> SeriesGrupos => Agrupacion is { } a
        ? a.Grupos.OrderBy(g => g.Grupo).Select(g => new SerieGrafico(NombreGrupo(g), Color(g.Grupo, a.K), [])).ToList()
        : [];

    /// <summary>Puntos del gráfico: valores reales de los ejes elegidos; el dato es el equipo.</summary>
    public IReadOnlyList<PuntoGrafico> Puntos => Agrupacion is { } a
        ? a.Equipos.Select(e => new PuntoGrafico(
                EjeX.Valor(e.Caracteristicas) ?? 0,
                EjeY.Valor(e.Caracteristicas) ?? 0,
                e.Grupo,
                e.PlacaInventario ?? "Equipo",
                e.TipoEquipo ?? string.Empty,
                e))
            .ToList()
        : [];

    /// <summary>«Tabla de datos»: un equipo por fila con su grupo y los valores de los ejes elegidos, del grupo con más fallas al de menos.</summary>
    public IReadOnlyList<FilaEquipoAnalitica> FilasTabla
    {
        get
        {
            if (Agrupacion is not { } a)
            {
                return [];
            }

            var nombres = a.Grupos.ToDictionary(g => g.Grupo, NombreGrupo);
            return a.Equipos
                .OrderByDescending(e => e.Grupo)
                .ThenBy(e => e.PlacaInventario, StringComparer.Ordinal)
                .Select(e => new FilaEquipoAnalitica(
                    e.PlacaInventario ?? "—",
                    string.IsNullOrWhiteSpace(e.TipoEquipo) ? "—" : e.TipoEquipo,
                    nombres.GetValueOrDefault(e.Grupo, $"Grupo {e.Grupo + 1}"),
                    Color(e.Grupo, a.K),
                    Numero(EjeX.Valor(e.Caracteristicas)),
                    Numero(EjeY.Valor(e.Caracteristicas)),
                    e))
                .ToList();
        }
    }

    /// <summary>Tarjetas por grupo, del que tiene más fallas al que tiene menos.</summary>
    public IReadOnlyList<FilaGrupo> Resumen => Agrupacion is { } a
        ? a.Grupos.OrderByDescending(g => g.Grupo).Select(g => new FilaGrupo(
            g.Grupo,
            NombreGrupo(g),
            Color(g.Grupo, a.K),
            g.Equipos == 1 ? "1 equipo" : $"{g.Equipos} equipos",
            Numero(OpcionesEje[0].Valor(g.Promedio)),
            g.Promedio.CostoConfirmado is { } c ? ((decimal)c).ToString("C0", Cultura) : "—",
            g.Promedio.TiempoMedioMin is { } t ? $"{t.ToString("0", Cultura)} min" : "—")).ToList()
        : [];

    public string ComoLeerlo => Agrupacion is { Grupos.Count: > 0 } a
        ? $"Los grupos se ordenan por número de fallas. «{NombreGrupo(a.Grupos.MaxBy(g => g.Grupo)!)}» reúne los equipos con más fallas: son candidatos para revisar con el comité de reposición. Pasa el mouse por un punto para ver el equipo"
            + (PuedeAbrirEquipos ? " y haz clic para abrir su hoja de vida." : ".")
            + (a.Inercia is { } i ? $" Inercia del modelo: {i.ToString("0.##", Cultura)} (más baja, grupos más compactos)." : string.Empty)
        : string.Empty;

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Resultado = await servicio.AgrupamientosAsync(CancellationToken.None);
            if (Agrupacion is null)
            {
                GruposSolicitados = Resultado.K;
            }

            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = error.Codigo == "ANA-001"
                ? new Aviso(
                    "Aún no hay resultados",
                    PuedeRecalcular ? "Todavía no se ha calculado la analítica de tu institución. Presiona «Recalcular» para calcularla ahora." : error.Message,
                    error.Codigo,
                    EsError: false,
                    PuedeReintentar: true)
                : new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos cargar la analítica", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
            Refrescar();
        }
    }

    private bool SePuedeRecalcular() => PuedeRecalcular && !Recalculando;

    /// <summary>Pide el recálculo y consulta el estado hasta que termina; entonces recarga el resultado.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeRecalcular))]
    private async Task RecalcularAsync()
    {
        Recalculando = true;
        Confirmacion = null;
        try
        {
            EstadoRecalculo estado;
            try
            {
                estado = await servicio.RecalcularAsync(CancellationToken.None);
            }
            catch (ErrorApi error) when (error.Codigo == "ANA-002")
            {
                // Otro usuario ya lo pidió: se espera ese mismo cálculo.
                estado = new EstadoRecalculo(EstadoRecalculo.EnCurso, null, null, null, null);
            }

            for (var i = 0; i < ConsultasMaximas && estado.Estado == EstadoRecalculo.EnCurso; i++)
            {
                await Task.Delay(IntervaloConsulta);
                estado = await servicio.EstadoRecalculoAsync(CancellationToken.None);
            }

            if (estado.Estado == EstadoRecalculo.Terminado)
            {
                await CargarAsync();
                if (Aviso is null)
                {
                    Confirmacion = new Aviso("Modelo recalculado", $"{Resultado?.Equipos.Count ?? 0} equipos agrupados con los datos de hoy.", null, EsError: false);
                }
            }
            else if (estado.Estado == EstadoRecalculo.Fallido)
            {
                Aviso = new Aviso("No pudimos recalcular", estado.Mensaje ?? "Intenta de nuevo más tarde.", null, PuedeReintentar: true);
            }
            else
            {
                Aviso = new Aviso("El cálculo sigue en curso", "Está tardando más de lo normal. Vuelve a consultar en unos minutos.", null, EsError: false, PuedeReintentar: true);
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos recalcular", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            Recalculando = false;
        }
    }

    /// <summary>Clic en un punto del gráfico o en una fila de la tabla: abre la hoja de vida del equipo.</summary>
    [RelayCommand]
    private void AbrirEquipo(object? origen)
    {
        var equipo = origen switch
        {
            PuntoGrafico { Dato: EquipoAgrupado e } => e,
            FilaEquipoAnalitica f => f.Equipo,
            _ => null,
        };
        if (equipo is not null && PuedeAbrirEquipos)
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(
                EquiposViewModel.IdPantalla, EquiposViewModel.ParametroDetalle(equipo.EquipoId, equipo.PlacaInventario ?? string.Empty)));
        }
    }

    private bool SePuedeExportar() => HayDatos && dialogo is not null;

    /// <summary>«Exportar resultado»: CSV (abre en Excel) con el grupo asignado a cada equipo.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeExportar))]
    private void Exportar()
    {
        if (dialogo is null || Agrupacion is not { } a)
        {
            return;
        }

        var ruta = dialogo.ElegirDestino($"agrupamiento_{a.K}_grupos.csv");
        if (ruta is null)
        {
            return;
        }

        var nombres = a.Grupos.ToDictionary(g => g.Grupo, NombreGrupo);
        var texto = new StringBuilder("Placa;Tipo;Grupo;Fallas (correctivos);Intervenciones;Proporción de correctivos (%);Costo confirmado (COP);Tiempo de atención (min)\r\n");
        foreach (var e in a.Equipos.OrderByDescending(e => e.Grupo).ThenBy(e => e.PlacaInventario, StringComparer.Ordinal))
        {
            var c = e.Caracteristicas;
            texto.Append(string.Join(';', Csv(e.PlacaInventario ?? string.Empty), Csv(e.TipoEquipo ?? string.Empty), Csv(nombres.GetValueOrDefault(e.Grupo, $"Grupo {e.Grupo + 1}")),
                Numero(OpcionesEje[0].Valor(c)), Numero(c.Intervenciones), Numero(OpcionesEje[4].Valor(c)), Numero(c.CostoConfirmado), Numero(c.TiempoMedioMin)));
            texto.Append("\r\n");
        }

        dialogo.Guardar(ruta, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())]);
        Confirmacion = new Aviso("Exportación lista", ruta, null, EsError: false);
    }

    private bool SePuedeExportarPdf() => HayAgrupacion && dialogo is not null && !ExportandoPdf;

    /// <summary>
    /// «Exportar PDF» (J-03): el número de grupos, los ejes y el gráfico de la pantalla van a la API, que arma el PDF con
    /// el último cálculo de la institución (grupos, equipos y «Cómo leerlo»).
    /// </summary>
    [RelayCommand(CanExecute = nameof(SePuedeExportarPdf))]
    private async Task ExportarPdfAsync()
    {
        if (dialogo is null || !HayAgrupacion)
        {
            return;
        }

        ExportandoPdf = true;
        Confirmacion = null;
        try
        {
            var graficos = GraficoPdf.De(CapturarGraficos?.Invoke() ?? []);
            var archivo = await servicio.AnaliticaPdfAsync(new SolicitudPdfAnalitica(GruposSolicitados, EjeX.Clave, EjeY.Clave, graficos), CancellationToken.None);
            (Confirmacion, var error) = GuardarArchivo.Guardar(dialogo, archivo);
            Aviso = error;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos generar el PDF", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            ExportandoPdf = false;
        }
    }

    partial void OnGruposSolicitadosChanged(int value) => Refrescar();

    partial void OnEjeXChanged(VariableAnalitica value) => Refrescar();

    partial void OnEjeYChanged(VariableAnalitica value) => Refrescar();

    partial void OnResultadoChanged(Agrupamiento? value) => Refrescar();

    private void Refrescar()
    {
        foreach (var propiedad in new[]
        {
            nameof(Agrupacion), nameof(HayAgrupacion), nameof(AvisoGrupos), nameof(TituloEjeX), nameof(TituloEjeY), nameof(TituloGrafico),
            nameof(SeriesGrupos), nameof(Puntos), nameof(FilasTabla), nameof(Resumen), nameof(ComoLeerlo),
        })
        {
            OnPropertyChanged(propiedad);
        }

        ExportarPdfCommand.NotifyCanExecuteChanged();
    }

    private static string NombreGrupo(GrupoAnalitica g) => string.IsNullOrWhiteSpace(g.Nombre) ? $"Grupo {g.Grupo + 1}" : g.Nombre;

    private static string Color(int grupo, int k) => Colores.TryGetValue(Math.Clamp(k, 1, 6), out var lista) && grupo >= 0 && grupo < lista.Length ? lista[grupo] : "Brush.Text3";

    private static string Titulo(VariableAnalitica v) => string.IsNullOrEmpty(v.Unidad) ? v.Nombre : $"{v.Nombre} ({v.Unidad})";

    private static string Numero(double? valor) => valor is { } v ? v.ToString("0.##", Cultura) : "—";

    private static string Csv(string valor) => valor.Contains(';') || valor.Contains('"') ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
}

/// <summary>Variable que se puede poner en un eje del gráfico de dispersión.</summary>
public sealed record VariableAnalitica(string Clave, string Nombre, string Unidad, Func<Caracteristicas, double?> Valor)
{
    public override string ToString() => Nombre;
}

/// <summary>Fila de «Tabla de datos»: placa, tipo, grupo (con su color) y los valores de los dos ejes elegidos.</summary>
public sealed record FilaEquipoAnalitica(string Placa, string Tipo, string Grupo, string ClaveColor, string ValorX, string ValorY, EquipoAgrupado Equipo);

/// <summary>Tarjeta de un grupo: nombre, color, cuántos equipos y sus promedios.</summary>
public sealed record FilaGrupo(int Grupo, string Nombre, string ClaveColor, string Equipos, string Fallas, string Costo, string Tiempo);
