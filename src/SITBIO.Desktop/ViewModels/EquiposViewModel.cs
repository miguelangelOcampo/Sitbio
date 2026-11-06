using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-005 Equipos biomédicos (HU-002; AC-01 a AC-05 y AC-07). Busca por código de activo o serie y
/// filtra por estado, clase de riesgo y calibración en la API (paginación del servidor). Al abrir un
/// equipo muestra su hoja de vida (UI-006) y, al volver, la lista conserva filtros y página (NAV-006).
/// </summary>
public partial class EquiposViewModel(IServicioEquipos servicio, TimeProvider tiempo) : ObservableObject
{
    public const int TamanoPagina = 20;

    private const string Calibra = "cal-si";
    private const string NoCalibra = "cal-no";

    private int _consulta;
    private bool _aplicandoVarios;

    public static IReadOnlyList<OpcionFiltro> OpcionesBuscarPor { get; } =
        [new("placa", "Código de activo"), new("serie", "Número de serie")];

    public static IReadOnlyList<OpcionFiltro> OpcionesEstado { get; } =
    [
        new(string.Empty, "Todo estado"),
        new("OPERATIVO", "Operativo"),
        new("EN_MANTENIMIENTO", "En mantenimiento"),
        new("FUERA_DE_SERVICIO", "Fuera de servicio"),
        new("BAJA", "Baja"),
    ];

    /// <summary>Filtro «Riesgo y calibración» del prototipo: una clase de riesgo o la necesidad de calibración.</summary>
    public static IReadOnlyList<OpcionFiltro> OpcionesRiesgo { get; } =
    [
        new(string.Empty, "Riesgo y calibración"),
        .. TextosEquipo.DescripcionClase.Select(c => new OpcionFiltro(c.Key, $"Clase {c.Key} · {c.Value.ToLower(TextosEquipo.Cultura)}")),
        new(Calibra, "Requiere calibración"),
        new(NoCalibra, "No requiere calibración"),
    ];

    // ---------- Filtros ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    private OpcionFiltro _buscarPor = OpcionesBuscarPor[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroEstado = OpcionesEstado[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroRiesgo = OpcionesRiesgo[0];

    public bool HayFiltros => Busqueda.Trim().Length > 0 || FiltroEstado.Valor.Length > 0 || FiltroRiesgo.Valor.Length > 0;

    public string TextoBusqueda => BuscarPor.Valor == "serie" ? "Buscar por número de serie" : "Buscar por código de activo";

    // ---------- Resultado ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEquipos), nameof(ResumenTotal))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEquipos), nameof(ResumenTotal))]
    private Aviso? _aviso;

    [ObservableProperty]
    private IReadOnlyList<FilaEquipo> _filas = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(ResumenTotal), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinEquipos))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    /// <summary>Hoja de vida abierta (UI-006); null = se ve la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnLista))]
    private DetalleEquipoViewModel? _detalle;

    public bool EnLista => Detalle is null;

    public bool SinEquipos => !Cargando && Aviso is null && Total == 0;

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenTotal => Cargando
        ? "Cargando resultados…"
        : $"{Total:N0} {(Total == 1 ? "equipo" : "equipos")}{(HayFiltros ? (Total == 1 ? " coincide con los filtros" : " coinciden con los filtros") : " en total")}";

    public string ResumenPagina => Total == 0
        ? string.Empty
        : $"Mostrando {(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0}";

    // ---------- Comandos ----------

    [RelayCommand]
    private Task CargarAsync() => ConsultarAsync(Pagina);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    [RelayCommand]
    private Task LimpiarAsync()
    {
        _aplicandoVarios = true;
        Busqueda = string.Empty;
        FiltroEstado = OpcionesEstado[0];
        FiltroRiesgo = OpcionesRiesgo[0];
        _aplicandoVarios = false;
        return ConsultarAsync(1);
    }

    /// <summary>NAV-006: abre la hoja de vida del equipo (doble clic o Enter en la fila).</summary>
    [RelayCommand]
    private Task AbrirAsync(FilaEquipo? fila)
    {
        if (fila is null)
        {
            return Task.CompletedTask;
        }

        var detalle = new DetalleEquipoViewModel(servicio, tiempo, fila.Equipo.Id, fila.Placa, Volver);
        Detalle = detalle;
        return detalle.CargarCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    // ---------- Apoyo ----------

    /// <summary>Vuelve a la lista con los mismos filtros y página.</summary>
    private void Volver() => Detalle = null;

    partial void OnBusquedaChanged(string value) => AlCambiarFiltro();

    partial void OnBuscarPorChanged(OpcionFiltro value)
    {
        OnPropertyChanged(nameof(TextoBusqueda));
        if (Busqueda.Trim().Length > 0)
        {
            AlCambiarFiltro();
        }
    }

    partial void OnFiltroEstadoChanged(OpcionFiltro value) => AlCambiarFiltro();

    partial void OnFiltroRiesgoChanged(OpcionFiltro value) => AlCambiarFiltro();

    private void AlCambiarFiltro()
    {
        if (!_aplicandoVarios)
        {
            _ = ConsultarAsync(1);
        }
    }

    /// <summary>Filtro para la API con los valores actuales de la pantalla.</summary>
    public FiltroEquipos Filtro(int pagina)
    {
        var texto = Busqueda.Trim();
        var riesgo = FiltroRiesgo?.Valor ?? string.Empty;
        return new FiltroEquipos(
            Placa: BuscarPor.Valor == "placa" && texto.Length > 0 ? texto : null,
            Serie: BuscarPor.Valor == "serie" && texto.Length > 0 ? texto : null,
            Estado: FiltroEstado?.Valor is { Length: > 0 } estado ? estado : null,
            Clase: riesgo is { Length: > 0 } and not Calibra and not NoCalibra ? riesgo : null,
            Calibracion: riesgo switch
            {
                Calibra => true,
                NoCalibra => false,
                _ => null,
            },
            Pagina: pagina,
            Tamano: TamanoPagina);
    }

    private async Task ConsultarAsync(int pagina)
    {
        // Si el usuario cambia los filtros antes de que llegue la respuesta, solo vale la última consulta.
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await servicio.ListarAsync(Filtro(pagina), CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            var hoy = DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);
            Filas = resultado.Items.Select(e => new FilaEquipo(e, hoy)).ToList();
            Total = resultado.Total;
            Pagina = pagina;
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            if (consulta != _consulta)
            {
                return;
            }

            Filas = [];
            Total = 0;
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos cargar el inventario",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargando = false;
            }
        }
    }
}

/// <summary>Fila de UI-005 con lo que la tabla muestra.</summary>
public sealed record FilaEquipo(Equipo Equipo, DateOnly Hoy)
{
    public string Placa => Equipo.PlacaInventario;

    public string Tipo => Equipo.TipoEquipo;

    public string Serie => Equipo.NumeroSerie;

    public string Clase => Equipo.ClaseRiesgo ?? TextosEquipo.SinDato;

    public string DescripcionClase => TextosEquipo.Clase(Equipo.ClaseRiesgo);

    public string Fabricante => Equipo.Marca;

    public string Ubicacion => Equipo.Ubicacion;

    public string Estado => Equipo.EstadoNombre;

    public TonoEstado Tono => TextosEquipo.Tono(Equipo.Estado);

    /// <summary>RN-010: «Registro vencido», «Registro por vencer» o «Sin registro»; null si está vigente.</summary>
    public string? AvisoRegistro => TextosEquipo.AvisoCorto(Equipo);

    public bool RegistroVencido => Equipo.AvisoRegistro is AvisosRegistro.Vencido or AvisosRegistro.SinDato;

    public string ProximoMantenimiento => TextosEquipo.Fecha(Equipo.Preventivo?.ProximaFecha);

    /// <summary>«Vencido» o «en N d» si faltan 7 días o menos (prototipo).</summary>
    public string? DetalleMantenimiento
    {
        get
        {
            if (Equipo.Preventivo is not { } plan || Equipo.FechaBaja is not null)
            {
                return null;
            }

            if (plan.Vencido)
            {
                return "Vencido";
            }

            var dias = plan.ProximaFecha.DayNumber - Hoy.DayNumber;
            return dias is >= 0 and <= 7 ? (dias == 0 ? "hoy" : $"en {dias} d") : null;
        }
    }
}
