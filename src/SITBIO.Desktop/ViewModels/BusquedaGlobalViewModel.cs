using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Búsqueda global de la barra superior (D-04; prototipo App.tsx, busqueda-global). Desde dos caracteres consulta
/// GET /busqueda y muestra en un popup los grupos Equipos, Tiquetes y Órdenes de trabajo. Con las flechas se
/// recorre la lista y Enter abre el detalle: la hoja de vida del equipo (UI-006), el seguimiento del tiquete
/// (UI-010) o la orden (UI-012, que abre la intervención o la revisión). Solo se muestran los grupos cuya pantalla
/// existe en el menú del rol; lo que cada rol puede ver lo decide la API.
/// </summary>
public partial class BusquedaGlobalViewModel : ObservableObject
{
    public const int LongitudMinima = 2;
    public const string GrupoEquipos = "Equipos";
    public const string GrupoTiquetes = "Tiquetes";
    public const string GrupoOrdenes = "Órdenes de trabajo";
    public const string Indicacion = "Buscar equipo, código de activo, tiquete u orden…";

    private static readonly TimeSpan RetardoPorDefecto = TimeSpan.FromMilliseconds(250);

    private readonly IServicioBusqueda _servicio;
    private readonly Func<string, bool> _pantallaDisponible;
    private readonly TimeSpan _retardo;
    private int _consulta;

    /// <param name="pantallaDisponible">Si el menú del rol tiene la pantalla (UI-005, UI-010, UI-012) que abre cada grupo.</param>
    /// <param name="retardo">Espera desde la última tecla antes de consultar (por defecto 250 ms).</param>
    public BusquedaGlobalViewModel(IServicioBusqueda servicio, Func<string, bool> pantallaDisponible, TimeSpan? retardo = null)
    {
        _servicio = servicio;
        _pantallaDisponible = pantallaDisponible;
        _retardo = retardo ?? RetardoPorDefecto;
    }

    /// <summary>El usuario eligió un resultado; el marco abre la pantalla con el dato.</summary>
    public event EventHandler<SolicitudNavegacion>? AperturaSolicitada;

    [ObservableProperty]
    private string _texto = string.Empty;

    /// <summary>La última consulta lanzada al escribir (con su retardo); permite esperarla.</summary>
    public Task BusquedaEnCurso { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Abierto), nameof(SinResultados))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Abierto), nameof(SinResultados))]
    private string? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Abierto), nameof(SinResultados))]
    private IReadOnlyList<GrupoResultados> _grupos = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Abierto), nameof(SinResultados))]
    private bool _consultado;

    [ObservableProperty]
    private ResultadoEncontrado? _seleccionado;

    /// <summary>Resultados en el orden en que se recorren con las flechas.</summary>
    public IReadOnlyList<ResultadoEncontrado> Resultados => Grupos.SelectMany(g => g.Resultados).ToList();

    public bool Buscable => Texto.Trim().Length >= LongitudMinima;

    /// <summary>La caja está vacía: se ve la indicación.</summary>
    public bool SinTexto => Texto.Length == 0;

    /// <summary>El popup se muestra desde dos caracteres.</summary>
    public bool Abierto => Buscable && (Cargando || Consultado || Aviso is not null);

    public bool SinResultados => Buscable && Consultado && !Cargando && Aviso is null && Grupos.Count == 0;

    public string TextoSinResultados => $"Sin resultados para «{Texto.Trim()}».";

    partial void OnTextoChanged(string value)
    {
        OnPropertyChanged(nameof(Buscable));
        OnPropertyChanged(nameof(SinTexto));
        OnPropertyChanged(nameof(Abierto));
        OnPropertyChanged(nameof(SinResultados));
        OnPropertyChanged(nameof(TextoSinResultados));
        BusquedaEnCurso = BuscarAsync(value.Trim());
    }

    partial void OnGruposChanged(IReadOnlyList<GrupoResultados> value)
    {
        OnPropertyChanged(nameof(Resultados));
        Seleccionado = Resultados.FirstOrDefault();
    }

    /// <summary>Flecha abajo: el siguiente resultado (al final vuelve al primero).</summary>
    [RelayCommand]
    private void SeleccionarSiguiente() => Mover(1);

    /// <summary>Flecha arriba: el resultado anterior (al principio pasa al último).</summary>
    [RelayCommand]
    private void SeleccionarAnterior() => Mover(-1);

    /// <summary>Enter o clic: abre el resultado (sin parámetro, el seleccionado) y limpia la caja.</summary>
    [RelayCommand]
    private void Abrir(ResultadoEncontrado? resultado)
    {
        var elegido = resultado ?? Seleccionado;
        if (elegido is null)
        {
            return;
        }

        Cerrar();
        AperturaSolicitada?.Invoke(this, elegido.Destino);
    }

    /// <summary>Escape: limpia la caja y cierra el popup.</summary>
    [RelayCommand]
    private void Cerrar() => Texto = string.Empty;

    private void Mover(int paso)
    {
        var resultados = Resultados;
        if (resultados.Count == 0)
        {
            return;
        }

        var actual = Seleccionado is null ? -1 : resultados.ToList().IndexOf(Seleccionado);
        var siguiente = actual < 0 ? (paso > 0 ? 0 : resultados.Count - 1) : (actual + paso + resultados.Count) % resultados.Count;
        Seleccionado = resultados[siguiente];
    }

    private async Task BuscarAsync(string texto)
    {
        var consulta = ++_consulta;
        if (texto.Length < LongitudMinima)
        {
            Cargando = false;
            Aviso = null;
            Consultado = false;
            Grupos = [];
            return;
        }

        Cargando = true;
        try
        {
            if (_retardo > TimeSpan.Zero)
            {
                await Task.Delay(_retardo);
                if (consulta != _consulta)
                {
                    return;
                }
            }

            var resultado = await _servicio.BuscarAsync(texto, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Aviso = null;
            Grupos = Agrupar(resultado);
            Consultado = true;
        }
        catch (ErrorApi error)
        {
            if (consulta != _consulta)
            {
                return;
            }

            Grupos = [];
            Aviso = error.SinConexion ? "Sin conexión con el servidor. Revisa tu red e intenta de nuevo." : error.Message;
            Consultado = true;
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargando = false;
            }
        }
    }

    private List<GrupoResultados> Agrupar(ResultadoBusqueda resultado)
    {
        var grupos = new List<GrupoResultados>();
        if (_pantallaDisponible(PantallaEquipos) && resultado.Equipos.Count > 0)
        {
            grupos.Add(new GrupoResultados(GrupoEquipos, resultado.Equipos.Select(e => new ResultadoEncontrado(
                GrupoEquipos,
                "Icono.Equipo",
                e.TipoEquipo,
                $"{e.Ubicacion} · Serie {e.NumeroSerie}",
                e.PlacaInventario,
                new SolicitudNavegacion(PantallaEquipos, EquiposViewModel.ParametroDetalle(e.Id, e.PlacaInventario)))).ToList()));
        }

        if (_pantallaDisponible(SeguimientoViewModel.IdPantalla) && resultado.Tiquetes.Count > 0)
        {
            grupos.Add(new GrupoResultados(GrupoTiquetes, resultado.Tiquetes.Select(t => new ResultadoEncontrado(
                GrupoTiquetes,
                "Icono.Riel.Solicitudes",
                t.TipoFalla,
                $"{t.PlacaInventario} · {EstadosTiquete.Nombre(t.Estado)}",
                t.Codigo,
                new SolicitudNavegacion(SeguimientoViewModel.IdPantalla, t.Codigo))).ToList()));
        }

        if (_pantallaDisponible(OrdenesViewModel.IdPantalla) && resultado.Ordenes.Count > 0)
        {
            grupos.Add(new GrupoResultados(GrupoOrdenes, resultado.Ordenes.Select(o => new ResultadoEncontrado(
                GrupoOrdenes,
                "Icono.Check",
                o.TipoMantenimiento == "correctivo" ? "Correctivo" : "Preventivo",
                $"{o.PlacaInventario} · {EstadosOrden.Nombre(o.Estado)}",
                o.Referencia,
                new SolicitudNavegacion(OrdenesViewModel.IdPantalla, o.Id.ToString()))).ToList()));
        }

        return grupos
            .Select(g => g with { Resultados = g.Resultados.Select((r, i) => r with { EncabezaGrupo = i == 0 }).ToList() })
            .ToList();
    }

    private const string PantallaEquipos = "UI-005";
}

public sealed record GrupoResultados(string Titulo, IReadOnlyList<ResultadoEncontrado> Resultados);

/// <summary>Una fila del popup: ícono, título (tipo de equipo, falla o mantenimiento), detalle y código a la derecha.</summary>
public sealed record ResultadoEncontrado(string Grupo, string Icono, string Titulo, string Detalle, string Codigo, SolicitudNavegacion Destino)
{
    /// <summary>El primero de su grupo lleva el título del grupo encima (el popup es una sola lista para las flechas).</summary>
    public bool EncabezaGrupo { get; init; }
}
