using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-010 Seguimiento de tiquetes (HU-017; FLU-004). Lista de tiquetes con filtro por estado, consulta
/// por código y detalle con el estado actual y el historial de cambios (CA-HU-017-01). Consultan AC-01 a
/// AC-04 y AC-07; el Personal Clínico (AC-04) solo ve los que reportó, porque así responde la API
/// (CA-HU-017-02). El Ingeniero Biomédico, que también reporta, puede ver solo los suyos.
/// </summary>
public partial class SeguimientoViewModel(IServicioTiquetes servicio, SesionActual sesion) : ObservableObject, IRecibeParametro
{
    public const string IdPantalla = "UI-010";
    public const int TamanoPagina = 20;
    public const string CodigoVacio = "Escribe el código del tiquete, por ejemplo TQT-000015.";
    public const string CodigoInvalido = "El código es TQT- seguido de 6 números, por ejemplo TQT-000015.";

    private const string PersonalClinico = "AC-04";
    private const string IngenieroBiomedico = "AC-02";

    private int _consulta;
    private int _apertura;

    public static IReadOnlyList<OpcionFiltro> OpcionesEstado { get; } =
        [new(string.Empty, "Todos los estados"), .. EstadosTiquete.Todos.Select(e => new OpcionFiltro(e, EstadosTiquete.Nombre(e)))];

    /// <summary>El Personal Clínico solo ve los tiquetes que reportó (lo garantiza la API).</summary>
    public bool SoloPropios => sesion.Sesion?.Rol == PersonalClinico;

    /// <summary>Quien también reporta fallas (AC-02) puede filtrar los que reportó.</summary>
    public bool PuedeVerSoloMios => sesion.Sesion?.Rol == IngenieroBiomedico;

    public string Titulo => SoloPropios ? "Mis tiquetes" : "Seguimiento de tiquetes";

    public string Descripcion => SoloPropios
        ? "Consulta en qué va la atención de las fallas que reportaste. Abre un tiquete para ver su historial."
        : "Consulta el estado de los tiquetes de tu institución y el historial de cada uno. Busca por código o elige uno de la lista.";

    // ---------- Filtros y búsqueda por código ----------

    [ObservableProperty]
    private OpcionFiltro _filtroEstado = OpcionesEstado[0];

    [ObservableProperty]
    private bool _soloMios;

    [ObservableProperty]
    private string _codigo = string.Empty;

    [ObservableProperty]
    private string? _errorCodigo;

    // ---------- Lista ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTiquetes))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTiquetes))]
    private Aviso? _aviso;

    [ObservableProperty]
    private IReadOnlyList<FilaTiquete> _filas = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinTiquetes))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    public bool Cargado { get; private set; }

    public bool SinTiquetes => Cargado && !Cargando && Aviso is null && Total == 0;

    public string TextoSinTiquetes => FiltroEstado.Valor.Length > 0 || SoloMios
        ? "No hay tiquetes con ese filtro."
        : SoloPropios ? "Aún no has reportado fallas." : "Tu institución aún no tiene tiquetes.";

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 tiquetes"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "tiquete" : "tiquetes")}";

    // ---------- Detalle ----------

    /// <summary>Tiquete abierto con su historial; null mientras se ve la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnLista))]
    private DetalleTiquete? _detalle;

    [ObservableProperty]
    private bool _abriendo;

    public bool EnLista => Detalle is null;

    // ---------- Comandos ----------

    [RelayCommand]
    private Task CargarAsync() => ConsultarAsync(1);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    /// <summary>CA-HU-017-01: consulta por código. Acepta minúsculas y solo el número («15» es TQT-000015).</summary>
    [RelayCommand]
    private Task ConsultarCodigoAsync()
    {
        var codigo = Normalizar(Codigo);
        if (codigo is null)
        {
            ErrorCodigo = string.IsNullOrWhiteSpace(Codigo) ? CodigoVacio : CodigoInvalido;
            return Task.CompletedTask;
        }

        ErrorCodigo = null;
        Codigo = codigo;
        return AbrirCodigoAsync(codigo, desdeBusqueda: true);
    }

    [RelayCommand]
    private Task AbrirAsync(FilaTiquete? fila) => fila is null ? Task.CompletedTask : AbrirCodigoAsync(fila.Codigo, desdeBusqueda: false);

    [RelayCommand]
    private void Volver()
    {
        Detalle = null;
        Aviso = null;
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    /// <summary>Llega de MSG-005 «Ver seguimiento» con el código recién creado.</summary>
    public void Recibir(string parametro)
    {
        if (Normalizar(parametro) is { } codigo)
        {
            _ = AbrirCodigoAsync(codigo, desdeBusqueda: false);
        }
    }

    /// <summary>TQT-###### en mayúsculas; acepta minúsculas, espacios y solo el número (hasta 6 dígitos). Null si no es un código.</summary>
    public static string? Normalizar(string? texto)
    {
        var limpio = texto?.Trim().ToUpperInvariant() ?? string.Empty;
        if (limpio.Length is > 0 and <= 6 && limpio.All(char.IsAsciiDigit))
        {
            return $"TQT-{int.Parse(limpio, CultureInfo.InvariantCulture):D6}";
        }

        return limpio.Length == 10 && limpio.StartsWith("TQT-", StringComparison.Ordinal) && limpio[4..].All(char.IsAsciiDigit)
            ? limpio
            : null;
    }

    // ---------- Apoyo ----------

    partial void OnFiltroEstadoChanged(OpcionFiltro value)
    {
        OnPropertyChanged(nameof(TextoSinTiquetes));
        _ = ConsultarAsync(1);
    }

    partial void OnSoloMiosChanged(bool value)
    {
        OnPropertyChanged(nameof(TextoSinTiquetes));
        _ = ConsultarAsync(1);
    }

    partial void OnCodigoChanged(string value) => ErrorCodigo = null;

    private async Task ConsultarAsync(int pagina)
    {
        var filtro = new FiltroTiquetes(FiltroEstado.Valor.Length > 0 ? FiltroEstado.Valor : null, PuedeVerSoloMios && SoloMios, pagina, TamanoPagina);

        // Si cambian los filtros antes de que llegue la respuesta, solo vale la última consulta.
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await servicio.ListarAsync(filtro, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Filas = resultado.Items.Select(t => new FilaTiquete(t)).ToList();
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
            Pagina = 1;
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar los tiquetes",
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
                OnPropertyChanged(nameof(SinTiquetes));
            }
        }
    }

    private async Task AbrirCodigoAsync(string codigo, bool desdeBusqueda)
    {
        var apertura = ++_apertura;
        Abriendo = true;
        try
        {
            var tiquete = servicio.ObtenerAsync(codigo, CancellationToken.None);
            var historial = servicio.HistorialAsync(codigo, CancellationToken.None);
            await Task.WhenAll(tiquete, historial);
            if (apertura != _apertura)
            {
                return;
            }

            Aviso = null;
            Detalle = new DetalleTiquete(await tiquete, await historial);
        }
        catch (ErrorApi error)
        {
            if (apertura != _apertura)
            {
                return;
            }

            if (desdeBusqueda && !error.SinConexion)
            {
                // Un código que no existe o que no se puede ver se explica junto al campo.
                ErrorCodigo = error.Message;
                return;
            }

            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : $"No se pudo abrir el tiquete {codigo}",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            if (apertura == _apertura)
            {
                Abriendo = false;
            }
        }
    }
}

/// <summary>Textos de criticidad y estado del tiquete, y tono del punto que los acompaña (texto, no solo color).</summary>
public static class TextosTiquete
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static string Criticidad(string criticidad) => criticidad switch
    {
        Criticidades.Critica => "Crítica",
        Criticidades.Alta => "Alta",
        Criticidades.Media => "Media",
        Criticidades.Baja => "Baja",
        _ => criticidad,
    };

    public static TonoEstado TonoCriticidad(string criticidad) => criticidad switch
    {
        Criticidades.Critica => TonoEstado.Critico,
        Criticidades.Alta => TonoEstado.Advertencia,
        _ => TonoEstado.Neutro,
    };

    /// <summary>Cerrado en verde; en ejecución y asignado en ámbar (en curso); recibido en gris (en espera).</summary>
    public static TonoEstado TonoEstadoTiquete(string estado) => estado switch
    {
        EstadosTiquete.Cerrado => TonoEstado.Exito,
        EstadosTiquete.Asignado or EstadosTiquete.EnEjecucion => TonoEstado.Advertencia,
        _ => TonoEstado.Neutro,
    };

    public static string Fecha(DateTimeOffset fecha) => fecha.ToLocalTime().ToString("d MMM yyyy", Cultura);

    public static string FechaHora(DateTimeOffset fecha) => fecha.ToLocalTime().ToString("d MMM yyyy · HH:mm", Cultura);
}

/// <summary>Fila de la lista de UI-010.</summary>
public sealed record FilaTiquete(Tiquete Tiquete)
{
    public string Codigo => Tiquete.Codigo;

    public string Equipo => $"{Tiquete.PlacaInventario} · {Tiquete.TipoEquipo}";

    public string Ubicacion => Tiquete.Ubicacion;

    public string TipoFalla => Tiquete.TipoFalla;

    public string Criticidad => TextosTiquete.Criticidad(Tiquete.Criticidad);

    public TonoEstado TonoCriticidad => TextosTiquete.TonoCriticidad(Tiquete.Criticidad);

    public string Estado => EstadosTiquete.Nombre(Tiquete.Estado);

    public TonoEstado Tono => TextosTiquete.TonoEstadoTiquete(Tiquete.Estado);

    public string Fecha => TextosTiquete.Fecha(Tiquete.FechaReporte);

    public string Reportante => Tiquete.Reportante;
}

/// <summary>Detalle de UI-010: datos del tiquete, estado actual e historial del más reciente al más antiguo.</summary>
public sealed class DetalleTiquete(Tiquete tiquete, IReadOnlyList<CambioTiquete> historial)
{
    public Tiquete Tiquete { get; } = tiquete;

    public string Codigo => Tiquete.Codigo;

    public string Estado => EstadosTiquete.Nombre(Tiquete.Estado);

    public TonoEstado Tono => TextosTiquete.TonoEstadoTiquete(Tiquete.Estado);

    public string Equipo => $"{Tiquete.PlacaInventario} · {Tiquete.TipoEquipo}";

    public string Serie => $"Serie {Tiquete.NumeroSerie} · {Tiquete.Ubicacion}";

    public string TipoFalla => Tiquete.TipoFalla;

    public string Criticidad => TextosTiquete.Criticidad(Tiquete.Criticidad);

    public TonoEstado TonoCriticidad => TextosTiquete.TonoCriticidad(Tiquete.Criticidad);

    public string Descripcion => string.IsNullOrWhiteSpace(Tiquete.Descripcion) ? "Sin descripción." : Tiquete.Descripcion;

    public string Reporte => $"{Tiquete.Reportante} · {TextosTiquete.FechaHora(Tiquete.FechaReporte)}";

    public string? Cierre => Tiquete.FechaCierre is { } cierre ? TextosTiquete.FechaHora(cierre) : null;

    /// <summary>RN-005: la falla crítica dejó el equipo «Fuera de servicio» hasta el cierre de la orden.</summary>
    public bool EquipoFueraDeServicio => Tiquete.EquipoFueraDeServicio;

    public string TextoFueraDeServicio => $"El equipo {Tiquete.PlacaInventario} está «Fuera de servicio» hasta que se cierre la orden.";

    /// <summary>Línea de tiempo, del cambio más reciente al más antiguo; el primero es el estado actual.</summary>
    public IReadOnlyList<PasoHistorial> Historial { get; } = historial
        .OrderByDescending(c => c.FechaCambio)
        .Select((c, i) => new PasoHistorial(c, EsActual: i == 0))
        .ToList();
}

/// <summary>Un cambio de la línea de tiempo del detalle.</summary>
public sealed record PasoHistorial(CambioTiquete Cambio, bool EsActual)
{
    public string Titulo => Cambio.EstadoAnterior is { } anterior
        ? $"{EstadosTiquete.Nombre(anterior)} → {EstadosTiquete.Nombre(Cambio.NuevoEstado)}"
        : EstadosTiquete.Nombre(Cambio.NuevoEstado);

    public TonoEstado Tono => TextosTiquete.TonoEstadoTiquete(Cambio.NuevoEstado);

    public string Fecha => TextosTiquete.FechaHora(Cambio.FechaCambio);

    public string Usuario => Cambio.Usuario;

    public string? Comentario => string.IsNullOrWhiteSpace(Cambio.Comentario) ? null : Cambio.Comentario;
}
