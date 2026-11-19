using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Historial de intervenciones (HU-005; opción HIST del menú). Tabla de las intervenciones de la institución,
/// de la más reciente a la más antigua, con filtros por equipo, tipo de equipo, tipo de mantenimiento y fechas,
/// y totales de todo el filtro. Cada fila abre la intervención en solo lectura (UI-013) o la hoja de vida del
/// equipo (UI-006). Consultan AC-01, AC-02, AC-03, AC-05 y AC-07; la API valida el rol y el tenant.
/// </summary>
public partial class HistorialIntervencionesViewModel(
    IServicioHistorialIntervenciones historial,
    IServicioCatalogos catalogos,
    IServicioOrdenes ordenes,
    IServicioEquipos equipos,
    IServicioConfiguracionCampos configuracion,
    SesionActual sesion) : ObservableObject, ISolicitaNavegacion
{
    public const string IdPantalla = "HIST";
    public const string PantallaEquipos = "UI-005";
    public const int TamanoPagina = 20;

    private static readonly string[] FormatosFecha = ["d/M/yyyy", "d-M-yyyy", "yyyy-M-d"];

    private int _consulta;
    private bool _aplicandoVarios;

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    public static OpcionFiltro TodoTipo { get; } = new(string.Empty, "Todos los tipos");

    public static IReadOnlyList<OpcionFiltro> OpcionesMantenimiento { get; } =
    [
        new(string.Empty, "Todo mantenimiento"),
        new(TiposMantenimientoHistorial.Preventivo, "Preventivos"),
        new(TiposMantenimientoHistorial.Correctivo, "Correctivos"),
    ];

    /// <summary>
    /// GET /ordenes/{id} lo consultan AC-01, AC-02, AC-03 y AC-07; el Gerente (AC-05) abre la hoja de vida.
    /// El técnico solo abre las órdenes que le asignaron: si la orden es de otro, la API responde 404 y se avisa.
    /// </summary>
    public bool PuedeVerIntervencion => sesion.Sesion?.Rol is "AC-01" or "AC-02" or "AC-03" or "AC-07";

    // ---------- Filtros ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _textoEquipo = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<OpcionFiltro> _opcionesTipo = [TodoTipo];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroTipo = TodoTipo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroMantenimiento = OpcionesMantenimiento[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _desde = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _hasta = string.Empty;

    [ObservableProperty]
    private string? _errorFechas;

    public bool HayFiltros => TextoEquipo.Trim().Length > 0 || FiltroTipo.Valor.Length > 0 || FiltroMantenimiento.Valor.Length > 0
        || Desde.Trim().Length > 0 || Hasta.Trim().Length > 0;

    // ---------- Resultado ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinIntervenciones))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinIntervenciones))]
    private Aviso? _aviso;

    [ObservableProperty]
    private IReadOnlyList<FilaHistorial> _filas = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervenciones), nameof(Correctivas), nameof(DetalleCorrectivas), nameof(CostoTotal), nameof(TiempoPromedio))]
    private ResumenHistorial _resumen = new(0, 0, 0, 0, null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinIntervenciones))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    /// <summary>UI-013 abierta en solo lectura; null = la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnLista))]
    private IntervencionViewModel? _intervencion;

    /// <summary>Fila cuya orden se está consultando para abrir la intervención.</summary>
    [ObservableProperty]
    private Guid? _abriendo;

    public bool EnLista => Intervencion is null;

    public bool Cargado { get; private set; }

    public bool SinIntervenciones => Cargado && !Cargando && Aviso is null && Total == 0;

    public string Intervenciones => Resumen.Total.ToString("N0", TextosEquipo.Cultura);

    public string Correctivas => Resumen.Correctivas.ToString("N0", TextosEquipo.Cultura);

    public string DetalleCorrectivas => Resumen.Preventivas == 1 ? "1 preventiva" : $"{Resumen.Preventivas:N0} preventivas";

    public string CostoTotal => Resumen.CostoTotal.ToString("C0", TextosEquipo.Cultura);

    public string TiempoPromedio => Resumen.TiempoPromedioMin is { } minutos ? TextosHistorial.Duracion(minutos) : "—";

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 intervenciones"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "intervención" : "intervenciones")}";

    // ---------- Comandos ----------

    /// <summary>Carga los tipos de equipo para el filtro y consulta la primera página.</summary>
    [RelayCommand]
    private async Task CargarAsync()
    {
        await CargarTiposAsync();
        await ConsultarAsync(1);
    }

    /// <summary>«Buscar» o Enter en la caja del equipo.</summary>
    [RelayCommand]
    private Task BuscarAsync() => ConsultarAsync(1);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    [RelayCommand]
    private Task LimpiarAsync()
    {
        _aplicandoVarios = true;
        TextoEquipo = string.Empty;
        FiltroTipo = TodoTipo;
        FiltroMantenimiento = OpcionesMantenimiento[0];
        Desde = string.Empty;
        Hasta = string.Empty;
        _aplicandoVarios = false;
        return ConsultarAsync(1);
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    /// <summary>Abre la hoja de vida del equipo (UI-006) en «Equipos biomédicos».</summary>
    [RelayCommand]
    private void AbrirHojaVida(FilaHistorial? fila)
    {
        if (fila is not null)
        {
            NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(
                PantallaEquipos, EquiposViewModel.ParametroDetalle(fila.Intervencion.EquipoId, fila.Intervencion.PlacaInventario)));
        }
    }

    /// <summary>Abre la intervención en solo lectura (UI-013): primero consulta la orden.</summary>
    [RelayCommand]
    private async Task AbrirIntervencionAsync(FilaHistorial? fila)
    {
        if (fila is null || !PuedeVerIntervencion || Abriendo is not null)
        {
            return;
        }

        Abriendo = fila.Intervencion.Id;
        try
        {
            var orden = await ordenes.ObtenerAsync(fila.Intervencion.OrdenId, CancellationToken.None);
            var detalle = new IntervencionViewModel(ordenes, equipos, configuracion, orden, false, _ => Intervencion = null);
            Intervencion = detalle;
            detalle.CargarCommand.Execute(null);
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : $"No se pudo abrir la intervención de {fila.Equipo}",
                error.SinConexion ? error.Message : $"{error.Message} Puedes consultarla en la hoja de vida del equipo.",
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Abriendo = null;
        }
    }

    // ---------- Apoyo ----------

    partial void OnFiltroTipoChanged(OpcionFiltro value) => AlCambiarFiltro();

    partial void OnFiltroMantenimientoChanged(OpcionFiltro value) => AlCambiarFiltro();

    partial void OnDesdeChanged(string value) => AlCambiarFiltro();

    partial void OnHastaChanged(string value) => AlCambiarFiltro();

    private void AlCambiarFiltro()
    {
        if (Cargado && !_aplicandoVarios)
        {
            _ = ConsultarAsync(1);
        }
    }

    /// <summary>Tipos de equipo del catálogo; si no se pueden leer, el filtro queda solo con «Todos».</summary>
    private async Task CargarTiposAsync()
    {
        try
        {
            var tipos = await catalogos.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None);
            _aplicandoVarios = true;
            OpcionesTipo = [TodoTipo, .. tipos.OrderBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(t => new OpcionFiltro(t.Id.ToString(), t.Nombre))];
            FiltroTipo = TodoTipo;
        }
        catch (ErrorApi)
        {
            OpcionesTipo = [TodoTipo];
        }
        finally
        {
            _aplicandoVarios = false;
        }
    }

    private async Task ConsultarAsync(int pagina)
    {
        if (!LeerFechas(out var desde, out var hasta))
        {
            return;
        }

        var filtro = new FiltroHistorial(
            TextoEquipo.Trim() is { Length: > 0 } texto ? texto : null,
            Guid.TryParse(FiltroTipo?.Valor, out var tipo) ? tipo : null,
            null,
            FiltroMantenimiento?.Valor is { Length: > 0 } mantenimiento ? mantenimiento : null,
            desde,
            hasta,
            pagina,
            TamanoPagina);

        // Si el usuario cambia los filtros antes de que llegue la respuesta, solo vale la última consulta.
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await historial.ConsultarAsync(filtro, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Filas = resultado.Items.Select(i => new FilaHistorial(i)).ToList();
            Resumen = resultado.Resumen;
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
            Resumen = new ResumenHistorial(0, 0, 0, 0, null);
            Total = 0;
            Pagina = 1;
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo consultar el historial",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargado = true;
                Cargando = false;
                OnPropertyChanged(nameof(SinIntervenciones));
            }
        }
    }

    /// <summary>Días del filtro (dd/mm/aaaa); la API incluye el día final completo.</summary>
    private bool LeerFechas(out DateOnly? desde, out DateOnly? hasta)
    {
        hasta = null;
        var okDesde = LeerFecha(Desde, out desde);
        var okHasta = LeerFecha(Hasta, out hasta);
        if (!okDesde || !okHasta)
        {
            ErrorFechas = "Escribe la fecha como dd/mm/aaaa, por ejemplo 05/11/2026.";
            return false;
        }

        if (desde is { } d && hasta is { } h && h < d)
        {
            ErrorFechas = "La fecha final no puede ser anterior a la inicial.";
            return false;
        }

        ErrorFechas = null;
        return true;
    }

    private static bool LeerFecha(string texto, out DateOnly? dia)
    {
        dia = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (DateOnly.TryParseExact(texto.Trim(), FormatosFecha, TextosEquipo.Cultura, DateTimeStyles.None, out var valor))
        {
            dia = valor;
            return true;
        }

        return false;
    }
}

/// <summary>Valores de <c>tipo_mantenimiento</c> que entiende GET /intervenciones.</summary>
public static class TiposMantenimientoHistorial
{
    public const string Preventivo = "preventivo";
    public const string Correctivo = "correctivo";
}

/// <summary>Textos del historial.</summary>
public static class TextosHistorial
{
    /// <summary>«45 min», «1 h», «1 h 30 min».</summary>
    public static string Duracion(double minutos)
    {
        var total = (int)Math.Round(minutos, MidpointRounding.AwayFromZero);
        var (horas, resto) = (total / 60, total % 60);
        return horas == 0 ? $"{resto} min" : resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
    }
}

/// <summary>Fila del historial con los textos de la tabla; el tipo va con punto y texto, no solo color (ACC-001).</summary>
public sealed record FilaHistorial(IntervencionHistorial Intervencion)
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    public string Fecha => Intervencion.FechaIntervencion.ToOffset(Colombia).ToString("dd/MM/yyyy", TextosEquipo.Cultura);

    public string Hora => Intervencion.FechaIntervencion.ToOffset(Colombia).ToString("HH:mm", TextosEquipo.Cultura);

    /// <summary>Código del tiquete de la correctiva; la preventiva no tiene tiquete.</summary>
    public string Referencia => Intervencion.CodigoTiquete ?? "Plan";

    public string Equipo => string.IsNullOrEmpty(Intervencion.TipoEquipo)
        ? Intervencion.PlacaInventario
        : $"{Intervencion.PlacaInventario} · {Intervencion.TipoEquipo}";

    /// <summary>Ícono del tipo de equipo.</summary>
    public string IconoTipo => IconosTipoEquipo.Clave(Intervencion.TipoEquipo);

    public bool EsCorrectivo => Intervencion.TipoMantenimiento == TiposMantenimientoHistorial.Correctivo;

    public string Tipo => EsCorrectivo ? "Correctivo" : "Preventivo";

    public string Tecnico => string.IsNullOrWhiteSpace(Intervencion.Tecnico) ? "—" : Intervencion.Tecnico;

    public string Diagnostico => Intervencion.Diagnostico;

    public string Salida => Intervencion.EstadoEquipoSalida ?? "Sin estado de salida";

    /// <summary>La intervención queda firmada cuando el Ingeniero aprueba la orden (HU-021).</summary>
    public string Firma => Intervencion.CerradaFirmada ? "Aprobada" : "Sin aprobar";

    public string Costo => Intervencion.CostoTotal.ToString("C0", TextosEquipo.Cultura);

    public string Tiempo => Intervencion.TiempoAtencionMin is { } minutos ? TextosHistorial.Duracion(minutos) : string.Empty;
}
