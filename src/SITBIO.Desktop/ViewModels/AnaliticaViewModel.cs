using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-023 Analítica (HU-008, HU-009): gráfico de dispersión de los equipos según tiempo medio de atención y costo,
/// coloreados por grupo K-Means, y tabla resumen por grupo (CA-HU-009-01). Si se piden más grupos que equipos,
/// pide reducirlos (CA-HU-009-02). Muestra el último resultado del lote (ADR-006), con su fecha.
/// </summary>
public partial class AnaliticaViewModel(IServicioAnalitica servicio) : ObservableObject
{
    public const string IdPantalla = "UI-023";

    /// <summary>Tamaño del área del gráfico en unidades de la vista.</summary>
    public const double Ancho = 640;

    public const double Alto = 300;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static IReadOnlyList<int> OpcionesGrupos { get; } = [2, 3, 4, 5, 6];

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDatos), nameof(Encabezado), nameof(Puntos), nameof(Resumen), nameof(EjeX), nameof(EjeY), nameof(AvisoGrupos))]
    private Agrupamiento? _resultado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoGrupos))]
    private int _gruposSolicitados = 3;

    public bool HayDatos => Resultado is { Equipos.Count: > 0 };

    public string Encabezado => Resultado is { } r
        ? $"{r.Equipos.Count} equipos y {r.IntervencionesAnalizadas} intervenciones · {r.K} grupos · calculado el {r.GeneradoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Cultura)}"
        : string.Empty;

    /// <summary>
    /// CA-HU-009-02: con menos equipos que grupos pide reducirlos; si el número no coincide con el del último lote,
    /// indica cómo recalcular.
    /// </summary>
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
                    "CA-HU-009-02");
            }

            return GruposSolicitados == r.K
                ? null
                : new Aviso(
                    "El resultado tiene otro número de grupos",
                    $"El último cálculo usó {r.K} grupos. Para ver {GruposSolicitados}, ejecuta el lote con «python -m sitbio_analytics -k {GruposSolicitados}».",
                    "ADR-006",
                    EsError: false);
        }
    }

    public string EjeX => Resultado is null ? string.Empty : $"Tiempo medio de atención (0 a {Maximo(e => e.TiempoMedioMin):0} min)";

    public string EjeY => Resultado is null ? string.Empty : $"Costo confirmado (0 a {Maximo(e => e.CostoConfirmado).ToString("C0", Cultura)})";

    /// <summary>Puntos del gráfico, ya escalados al área de <see cref="Ancho"/> × <see cref="Alto"/>.</summary>
    public IReadOnlyList<PuntoAnalitica> Puntos
    {
        get
        {
            if (Resultado is not { } r)
            {
                return [];
            }

            var maxX = Maximo(e => e.TiempoMedioMin);
            var maxY = Maximo(e => e.CostoConfirmado);
            return r.Equipos.Select(e => new PuntoAnalitica(
                (e.Caracteristicas.TiempoMedioMin ?? 0) / maxX * (Ancho - PuntoAnalitica.Diametro),
                Alto - PuntoAnalitica.Diametro - ((e.Caracteristicas.CostoConfirmado ?? 0) / maxY * (Alto - PuntoAnalitica.Diametro)),
                e.Grupo,
                $"{e.PlacaInventario ?? "Equipo"} · grupo {e.Grupo + 1} · {Minutos(e.Caracteristicas.TiempoMedioMin)} · {Pesos(e.Caracteristicas.CostoConfirmado)}"))
                .ToList();
        }
    }

    public IReadOnlyList<FilaGrupo> Resumen => Resultado?.Grupos.Select(g => new FilaGrupo(
        g.Grupo,
        $"Grupo {g.Grupo + 1}",
        g.Equipos.ToString(Cultura),
        g.Promedio.Intervenciones is { } n ? n.ToString("0.#", Cultura) : "—",
        g.Promedio.ProporcionCorrectivos is { } p ? $"{(p * 100).ToString("0", Cultura)} %" : "—",
        Pesos(g.Promedio.CostoConfirmado),
        Minutos(g.Promedio.TiempoMedioMin))).ToList() ?? [];

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Resultado = await servicio.AgrupamientosAsync(CancellationToken.None);
            GruposSolicitados = Resultado.K;
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = error.Codigo == "ANA-001"
                ? new Aviso("Aún no hay resultados", error.Message, error.Codigo, EsError: false, PuedeReintentar: true)
                : new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos cargar la analítica", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    private double Maximo(Func<Caracteristicas, double?> valor)
    {
        var maximo = Resultado?.Equipos.Select(e => valor(e.Caracteristicas) ?? 0).DefaultIfEmpty(0).Max() ?? 0;
        return maximo <= 0 ? 1 : maximo;
    }

    private static string Pesos(double? valor) => valor is { } v ? ((decimal)v).ToString("C0", Cultura) : "—";

    private static string Minutos(double? valor) => valor is { } v ? $"{v.ToString("0", Cultura)} min" : "—";
}

/// <summary>Punto del gráfico de dispersión; el color sale del grupo.</summary>
public sealed record PuntoAnalitica(double X, double Y, int Grupo, string Etiqueta)
{
    public const double Diametro = 12;
}

/// <summary>Fila de la tabla resumen por grupo.</summary>
public sealed record FilaGrupo(int Grupo, string Nombre, string Equipos, string Intervenciones, string Correctivos, string Costo, string Tiempo);
