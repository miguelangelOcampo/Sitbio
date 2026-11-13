using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-011 Bandeja de tiquetes y asignación (HU-004, HU-018; FLU-004). Lista los tiquetes «recibidos» de lo
/// más crítico a lo menos crítico (y, a igual criticidad, lo que más lleva esperando) y abre un panel
/// lateral para asignar la orden a un técnico activo, mostrando cuántas órdenes abiertas tiene cada uno
/// (RN-003). Solo el Ingeniero Biomédico (AC-02); la API valida el permiso y RN-003. Al asignar, el panel se
/// cierra y la fila pasa a «asignado» (NAV-008).
/// </summary>
public partial class BandejaViewModel(IServicioTiquetes tiquetes, IServicioOrdenes ordenes, TimeProvider tiempo) : ObservableObject
{
    public const string IdPantalla = "UI-011";
    public const int TamanoPagina = 20;
    public const string TecnicoObligatorio = "Elige el técnico que ejecutará la orden.";
    public const string FechaInvalida = "Escribe la fecha como dd/mm/aaaa.";
    public const string FechaPasada = "La fecha programada no puede ser anterior a hoy.";
    public const string SinTecnicosActivos =
        "Tu institución no tiene técnicos de mantenimiento activos. Pide al Administrador que cree o reactive uno (RN-003).";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private int _consulta;

    public static IReadOnlyList<OpcionFiltro> OpcionesPrioridad { get; } =
        [.. new[] { Criticidades.Critica, Criticidades.Alta, Criticidades.Media, Criticidades.Baja }.Select(c => new OpcionFiltro(c, TextosTiquete.Criticidad(c)))];

    // ---------- Lista ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTiquetes))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTiquetes))]
    private Aviso? _aviso;

    /// <summary>Confirmación de la última asignación (se ve sobre la lista).</summary>
    [ObservableProperty]
    private string? _exito;

    [ObservableProperty]
    private IReadOnlyList<FilaBandeja> _filas = [];

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

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 tiquetes por asignar"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "tiquete por asignar" : "tiquetes por asignar")}";

    // ---------- Técnicos ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTecnicos))]
    private IReadOnlyList<FilaTecnico> _tecnicos = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinTecnicos))]
    private string? _errorTecnicos;

    /// <summary>RN-003: sin técnicos activos no hay a quién asignar.</summary>
    public bool SinTecnicos => _tecnicosCargados && ErrorTecnicos is null && Tecnicos.Count == 0;

    private bool _tecnicosCargados;

    // ---------- Panel de asignación ----------

    /// <summary>Tiquete que se está asignando; null con el panel cerrado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelAbierto))]
    [NotifyCanExecuteChangedFor(nameof(AsignarCommand))]
    private FilaBandeja? _seleccionado;

    public bool PanelAbierto => Seleccionado is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AsignarCommand))]
    private FilaTecnico? _tecnicoElegido;

    [ObservableProperty]
    private OpcionFiltro _prioridad = OpcionesPrioridad[0];

    [ObservableProperty]
    private string _fechaProgramada = string.Empty;

    [ObservableProperty]
    private string? _errorTecnico;

    [ObservableProperty]
    private string? _errorFecha;

    [ObservableProperty]
    private string? _errorPrioridad;

    /// <summary>Error de la asignación que no es de un campo (conflicto, permiso, servidor).</summary>
    [ObservableProperty]
    private Aviso? _avisoPanel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AsignarCommand))]
    private bool _asignando;

    // ---------- Comandos ----------

    [RelayCommand]
    private Task CargarAsync() => Task.WhenAll(ConsultarAsync(1), CargarTecnicosAsync());

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    /// <summary>Abre el panel para un tiquete «recibido», con la prioridad sugerida por su criticidad (CA-HU-018-01).</summary>
    [RelayCommand]
    private void AbrirAsignacion(FilaBandeja? fila)
    {
        if (fila is null || !fila.PorAsignar)
        {
            return;
        }

        Seleccionado = fila;
        TecnicoElegido = null;
        Prioridad = OpcionesPrioridad.FirstOrDefault(o => o.Valor == fila.Tiquete.PrioridadSugerida) ?? OpcionesPrioridad[0];
        FechaProgramada = string.Empty;
        ErrorTecnico = ErrorFecha = ErrorPrioridad = null;
        AvisoPanel = null;
        Exito = null;
    }

    [RelayCommand]
    private void CerrarPanel()
    {
        Seleccionado = null;
        AvisoPanel = null;
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand]
    private void CerrarExito() => Exito = null;

    /// <summary>«Asignar y crear orden» (CA-HU-004-01). RN-003 lo vuelve a validar la API.</summary>
    [RelayCommand(CanExecute = nameof(PuedeAsignar))]
    private async Task AsignarAsync()
    {
        if (Seleccionado is not { } fila)
        {
            return;
        }

        ErrorTecnico = TecnicoElegido is null ? TecnicoObligatorio : null;
        DateOnly? programada = null;
        if (!string.IsNullOrWhiteSpace(FechaProgramada))
        {
            programada = FormularioEquipoViewModel.Fecha(FechaProgramada);
            ErrorFecha = programada is null ? FechaInvalida : programada < Hoy() ? FechaPasada : null;
        }
        else
        {
            ErrorFecha = null;
        }

        ErrorPrioridad = null;
        AvisoPanel = null;
        if (ErrorTecnico is not null || ErrorFecha is not null)
        {
            return;
        }

        var tecnico = TecnicoElegido!;
        Asignando = true;
        try
        {
            var orden = await ordenes.AsignarAsync(new DatosOrden(fila.Tiquete.Id, tecnico.Id, Prioridad.Valor, programada), CancellationToken.None);

            // NAV-008: el panel se cierra y la fila cambia a «asignado»; el técnico suma una orden abierta.
            Filas = Filas.Select(f => f.Tiquete.Id == fila.Tiquete.Id ? f.Asignada(orden.Tecnico) : f).ToList();
            Tecnicos = Tecnicos
                .Select(t => t.Id == tecnico.Id ? t with { OrdenesAbiertas = t.OrdenesAbiertas + 1 } : t)
                .OrderBy(t => t.OrdenesAbiertas).ThenBy(t => t.Nombre, StringComparer.Create(Cultura, ignoreCase: true))
                .ToList();
            Seleccionado = null;
            Exito = $"Orden creada para {orden.Tecnico}. El tiquete {fila.Codigo} pasó a «Asignado» y la orden ya está en su panel.";
        }
        catch (ErrorApi error)
        {
            if (error.Errores.Count > 0)
            {
                ErrorTecnico = error.Errores.GetValueOrDefault("tecnicoId");
                ErrorFecha = error.Errores.GetValueOrDefault("fechaProgramada");
                ErrorPrioridad = error.Errores.GetValueOrDefault("prioridad");
            }

            if (error.Errores.Count == 0 || (ErrorTecnico ?? ErrorFecha ?? ErrorPrioridad) is null)
            {
                AvisoPanel = new Aviso(
                    error.SinConexion ? "Sin conexión" : $"No se pudo asignar el tiquete {fila.Codigo}",
                    error.Message,
                    error.SinConexion ? "MSG-011" : error.Codigo);
            }

            // Otro ingeniero lo asignó primero: la fila ya no está por asignar.
            if (error.Codigo == "OT-001")
            {
                Filas = Filas.Select(f => f.Tiquete.Id == fila.Tiquete.Id ? f.Asignada(null) : f).ToList();
            }
        }
        finally
        {
            Asignando = false;
        }
    }

    private bool PuedeAsignar() => Seleccionado is not null && !Asignando;

    // ---------- Apoyo ----------

    partial void OnTecnicoElegidoChanged(FilaTecnico? value)
    {
        if (value is not null)
        {
            ErrorTecnico = null;
        }
    }

    partial void OnFechaProgramadaChanged(string value) => ErrorFecha = null;

    private DateOnly Hoy() => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

    private async Task ConsultarAsync(int pagina)
    {
        var filtro = new FiltroTiquetes(EstadosTiquete.Recibido, Pagina: pagina, Tamano: TamanoPagina, PorCriticidad: true);
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await tiquetes.ListarAsync(filtro, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Filas = resultado.Items.Select(t => new FilaBandeja(t)).ToList();
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
                error.SinConexion ? "Sin conexión" : "No se pudo consultar la bandeja",
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

    private async Task CargarTecnicosAsync()
    {
        try
        {
            Tecnicos = (await ordenes.TecnicosAsync(CancellationToken.None)).Select(t => new FilaTecnico(t.Id, t.Nombre, t.OrdenesAbiertas)).ToList();
            ErrorTecnicos = null;
        }
        catch (ErrorApi error)
        {
            Tecnicos = [];
            ErrorTecnicos = error.Message;
        }
        finally
        {
            _tecnicosCargados = true;
            OnPropertyChanged(nameof(SinTecnicos));
        }
    }
}

/// <summary>Fila de la bandeja: el tiquete y, tras asignarlo, a quién se asignó.</summary>
public sealed record FilaBandeja(Tiquete Tiquete, string? AsignadoA = null)
{
    public string Codigo => Tiquete.Codigo;

    public string Equipo => $"{Tiquete.PlacaInventario} · {Tiquete.TipoEquipo}";

    public string Ubicacion => Tiquete.Ubicacion;

    public string TipoFalla => Tiquete.TipoFalla;

    public string Criticidad => TextosTiquete.Criticidad(Tiquete.Criticidad);

    public TonoEstado TonoCriticidad => TextosTiquete.TonoCriticidad(Tiquete.Criticidad);

    /// <summary>Hace cuánto espera, para priorizar a igual criticidad.</summary>
    public string Reporte => $"{TextosTiquete.FechaHora(Tiquete.FechaReporte)} · {Tiquete.Reportante}";

    public string Estado => AsignadoA is { } tecnico ? $"Asignado a {tecnico}" : EstadosTiquete.Nombre(Tiquete.Estado);

    public TonoEstado Tono => TextosTiquete.TonoEstadoTiquete(Tiquete.Estado);

    /// <summary>Solo los «recibidos» se asignan (NAV-008).</summary>
    public bool PorAsignar => Tiquete.Estado == EstadosTiquete.Recibido;

    /// <summary>RN-005: la falla crítica dejó el equipo «Fuera de servicio».</summary>
    public bool EquipoFueraDeServicio => Tiquete.EquipoFueraDeServicio;

    public FilaBandeja Asignada(string? tecnico) => new(Tiquete with { Estado = EstadosTiquete.Asignado }, tecnico);
}

/// <summary>Técnico del panel con su carga en texto.</summary>
public sealed record FilaTecnico(Guid Id, string Nombre, int OrdenesAbiertas)
{
    public string Carga => OrdenesAbiertas switch
    {
        0 => "Sin órdenes abiertas",
        1 => "1 orden abierta",
        _ => $"{OrdenesAbiertas} órdenes abiertas",
    };

    public override string ToString() => Nombre;
}
