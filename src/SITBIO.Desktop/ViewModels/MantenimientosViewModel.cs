using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-015 Mantenimientos (HU-019; FLU-008): pestaña «Planes preventivos» con los planes de la institución de la
/// próxima fecha más cercana a la más lejana, y un panel lateral para definir la periodicidad y la próxima fecha
/// del equipo (CA-HU-019-01). Si se indica el último preventivo y no el próximo, el próximo es último +
/// periodicidad. Solo el Ingeniero Biomédico (AC-02) crea, edita, suspende, reactiva o finaliza; AC-01, AC-03,
/// AC-05 y AC-07 consultan. La pestaña «Calibraciones» es UI-017 (HU-022, <see cref="CalibracionesViewModel"/>).
/// Desde una alerta (UI-016, NAV-011) llega «plan:{id}» o «calibracion:{equipo}».
/// </summary>
public partial class MantenimientosViewModel(IServicioMantenimiento servicio, IServicioEquipos equipos, SesionActual sesion, TimeProvider tiempo)
    : ObservableObject, IRecibeParametro
{
    public const string IdPantalla = "UI-015";
    public const int TamanoPagina = 20;
    public const int MinimoBusqueda = 2;
    public const int MaximoDescripcion = 300;
    public const string SinEquipo = "Selecciona el equipo del plan.";
    public const string PeriodicidadInvalida = "Escribe cada cuántos días se hace el preventivo (1 o más).";
    public const string FechaInvalida = "Escribe la fecha como dd/mm/aaaa.";
    public const string UltimaFutura = "El último preventivo no puede ser una fecha futura.";
    public const string SinFechas = "Indica el último preventivo o la fecha del próximo.";
    public const string ProximaPasada = "El próximo preventivo no puede ser una fecha pasada.";
    public const string ProximaAntesDeUltima = "El próximo preventivo debe ser posterior al último.";
    public const string PrefijoPlan = "plan:";
    public const string PrefijoCalibracion = "calibracion:";

    private const string EstadoBaja = "BAJA";
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private int _consulta;
    private bool _eligiendo;
    private CancellationTokenSource? _cancelacionBusqueda;

    public static IReadOnlyList<OpcionFiltro> OpcionesEstado { get; } =
    [
        new(string.Empty, "Todos los estados"),
        new(EstadosPlan.Activo, "Activos"),
        new(EstadosPlan.Suspendido, "Suspendidos"),
        new(EstadosPlan.Finalizado, "Finalizados"),
    ];

    /// <summary>Solo AC-02 gestiona los planes (RNF-003; la API lo valida de nuevo).</summary>
    public bool PuedeGestionar => sesion.Sesion?.Rol == "AC-02";

    public bool SoloConsulta => !PuedeGestionar;

    /// <summary>Pestaña «Calibraciones» (UI-017).</summary>
    public CalibracionesViewModel Calibraciones { get; } = new(servicio, equipos, sesion, tiempo);

    // ---------- Pestañas ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnPlanes), nameof(PuedeCrearPlan))]
    private bool _enCalibraciones;

    public bool EnPlanes => !EnCalibraciones;

    /// <summary>«Nuevo plan» solo en la pestaña de planes.</summary>
    public bool PuedeCrearPlan => PuedeGestionar && EnPlanes;

    [RelayCommand]
    private void MostrarPlanes() => EnCalibraciones = false;

    [RelayCommand]
    private void MostrarCalibraciones()
    {
        EnCalibraciones = true;
        CerrarPanel();
        if (!Calibraciones.Cargado && !Calibraciones.Cargando)
        {
            Calibraciones.CargarCommand.Execute(null);
        }
    }

    // ---------- Lista ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlanes))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinPlanes))]
    private Aviso? _aviso;

    [ObservableProperty]
    private string? _exito;

    [ObservableProperty]
    private IReadOnlyList<FilaPlan> _filas = [];

    [ObservableProperty]
    private OpcionFiltro _filtroEstado = OpcionesEstado[1];

    /// <summary>Equipo al que se limitó la lista (al llegar desde una alerta); null = todos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltroEquipo), nameof(TextoFiltroEquipo))]
    private (Guid Id, string Placa)? _filtroEquipo;

    public bool HayFiltroEquipo => FiltroEquipo is not null;

    public string TextoFiltroEquipo => FiltroEquipo is { } f ? $"Solo el equipo {f.Placa}" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinPlanes))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    public bool Cargado { get; private set; }

    public bool SinPlanes => Cargado && !Cargando && Aviso is null && Total == 0;

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 planes"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "plan" : "planes")}";

    // ---------- Panel del plan ----------

    /// <summary>Plan en edición; null con el panel en «Nuevo plan» o cerrado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsNuevo), nameof(TituloPanel))]
    private PlanMantenimiento? _editando;

    [ObservableProperty]
    private bool _panelAbierto;

    public bool EsNuevo => Editando is null;

    public string TituloPanel => Editando is { } plan ? $"Plan de {plan.PlacaInventario}" : "Nuevo plan preventivo";

    [ObservableProperty]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCoincidencias), nameof(SinCoincidencias))]
    private IReadOnlyList<Equipo> _coincidencias = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCoincidencias))]
    private bool _busquedaHecha;

    [ObservableProperty]
    private bool _buscando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEquipo), nameof(TextoEquipo), nameof(SinCoincidencias))]
    private Equipo? _equipoSeleccionado;

    public bool HayCoincidencias => Coincidencias.Count > 0;

    public bool SinCoincidencias => BusquedaHecha && !HayCoincidencias && EquipoSeleccionado is null;

    public bool HayEquipo => EquipoSeleccionado is not null;

    public string TextoEquipo => EquipoSeleccionado is { } e ? $"{e.PlacaInventario} · {e.TipoEquipo} · {e.Ubicacion}" : string.Empty;

    [ObservableProperty]
    private string _descripcion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProximoCalculado))]
    private string _periodicidad = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProximoCalculado))]
    private string _ultimaEjecucion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProximoCalculado))]
    private string _proximaFecha = string.Empty;

    [ObservableProperty]
    private string? _errorEquipo;

    [ObservableProperty]
    private string? _errorDescripcion;

    [ObservableProperty]
    private string? _errorPeriodicidad;

    [ObservableProperty]
    private string? _errorUltima;

    [ObservableProperty]
    private string? _errorProxima;

    [ObservableProperty]
    private Aviso? _avisoPanel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand), nameof(CambiarEstadoCommand))]
    private bool _guardando;

    /// <summary>Finalizar no se deshace: se pide confirmar.</summary>
    [ObservableProperty]
    private FilaPlan? _confirmandoFinalizar;

    /// <summary>Sin próximo indicado: lo que calculará la API (último + periodicidad).</summary>
    public string ProximoCalculado
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ProximaFecha) || !int.TryParse(Periodicidad.Trim(), NumberStyles.None, Cultura, out var dias) || dias < 1)
            {
                return string.Empty;
            }

            if (FormularioEquipoViewModel.Fecha(UltimaEjecucion) is not { } ultima)
            {
                return string.Empty;
            }

            var proxima = ultima.AddDays(dias);
            return proxima < Hoy()
                ? $"Próximo preventivo: {TextosEquipo.Fecha(proxima)}. Ya pasó: el plan nacerá vencido y la orden se generará al consultar las alertas."
                : $"Próximo preventivo: {TextosEquipo.Fecha(proxima)}.";
        }
    }

    // ---------- Comandos de la lista ----------

    [RelayCommand]
    private Task CargarAsync() => ConsultarAsync(1);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    [RelayCommand]
    private Task QuitarFiltroEquipoAsync()
    {
        FiltroEquipo = null;
        return ConsultarAsync(1);
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand]
    private void CerrarExito() => Exito = null;

    partial void OnFiltroEstadoChanged(OpcionFiltro value) => CargarCommand.Execute(null);

    /// <summary>NAV-011: la alerta abre el plan (o la pestaña de calibraciones) sin perder la lista.</summary>
    public void Recibir(string parametro)
    {
        if (parametro.StartsWith(PrefijoCalibracion, StringComparison.Ordinal))
        {
            EnCalibraciones = true;
            CerrarPanel();
            if (Guid.TryParse(parametro[PrefijoCalibracion.Length..], out var equipo))
            {
                _ = Calibraciones.MostrarEquipoAsync(equipo);
            }
            else
            {
                Calibraciones.CargarCommand.Execute(null);
            }

            return;
        }

        if (parametro.StartsWith(PrefijoPlan, StringComparison.Ordinal) && Guid.TryParse(parametro[PrefijoPlan.Length..], out var id))
        {
            EnCalibraciones = false;
            _ = AbrirDesdeAlertaAsync(id);
        }
    }

    private async Task AbrirDesdeAlertaAsync(Guid id)
    {
        try
        {
            var plan = await servicio.ObtenerPlanAsync(id, CancellationToken.None);
            FiltroEquipo = (plan.EquipoId, plan.PlacaInventario);
            if (FiltroEstado != OpcionesEstado[0])
            {
                // Cambiar el filtro ya consulta la lista.
                FiltroEstado = OpcionesEstado[0];
            }
            else
            {
                await ConsultarAsync(1);
            }

            if (PuedeGestionar && plan.Estado != EstadosPlan.Finalizado)
            {
                AbrirEdicion(new FilaPlan(plan, Hoy()));
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo abrir el plan de la alerta",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
    }

    // ---------- Panel ----------

    [RelayCommand]
    private void NuevoPlan()
    {
        if (!PuedeGestionar)
        {
            return;
        }

        Editando = null;
        _eligiendo = true;
        Busqueda = string.Empty;
        _eligiendo = false;
        (EquipoSeleccionado, Coincidencias, BusquedaHecha) = (null, [], false);
        Descripcion = Periodicidad = UltimaEjecucion = ProximaFecha = string.Empty;
        LimpiarErrores();
        Exito = null;
        PanelAbierto = true;
    }

    [RelayCommand]
    private void AbrirEdicion(FilaPlan? fila)
    {
        if (fila is null || !PuedeGestionar || !fila.Editable)
        {
            return;
        }

        var plan = fila.Plan;
        Editando = plan;
        Descripcion = plan.Descripcion;
        Periodicidad = plan.PeriodicidadDias.ToString(Cultura);
        UltimaEjecucion = plan.UltimaEjecucion is { } u ? TextosEquipo.Fecha(u) : string.Empty;
        ProximaFecha = TextosEquipo.Fecha(plan.ProximaFecha);
        LimpiarErrores();
        Exito = null;
        PanelAbierto = true;
    }

    [RelayCommand]
    private void CerrarPanel()
    {
        PanelAbierto = false;
        Editando = null;
        AvisoPanel = null;
    }

    /// <summary>CMP-UI-003: busca por placa y por serie; no ofrece equipos dados de baja.</summary>
    [RelayCommand]
    private async Task BuscarEquiposAsync()
    {
        _cancelacionBusqueda?.Cancel();
        var texto = Busqueda.Trim();
        if (texto.Length < MinimoBusqueda)
        {
            Coincidencias = [];
            BusquedaHecha = false;
            return;
        }

        using var cancelacion = new CancellationTokenSource();
        _cancelacionBusqueda = cancelacion;
        Buscando = true;
        try
        {
            var porPlaca = await equipos.ListarAsync(new FiltroEquipos(Placa: texto, Tamano: 8), cancelacion.Token);
            var porSerie = await equipos.ListarAsync(new FiltroEquipos(Serie: texto, Tamano: 8), cancelacion.Token);
            if (!cancelacion.IsCancellationRequested)
            {
                Coincidencias = porPlaca.Items.Concat(porSerie.Items)
                    .Where(e => e.Estado != EstadoBaja)
                    .DistinctBy(e => e.Id)
                    .Take(8)
                    .ToList();
                BusquedaHecha = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Llegó otra búsqueda: esta ya no importa.
        }
        catch (ErrorApi error)
        {
            Coincidencias = [];
            AvisoPanel = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos buscar el equipo", error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            if (cancelacion == _cancelacionBusqueda)
            {
                _cancelacionBusqueda = null;
                Buscando = false;
            }
        }
    }

    [RelayCommand]
    private void ElegirEquipo(Equipo equipo)
    {
        _eligiendo = true;
        Busqueda = equipo.PlacaInventario;
        _eligiendo = false;
        EquipoSeleccionado = equipo;
        Coincidencias = [];
        BusquedaHecha = false;
        ErrorEquipo = null;
    }

    /// <summary>«Guardar plan» (CA-HU-019-01). La API valida de nuevo y responde junto al campo.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (!Validar(out var dias, out var ultima, out var proxima))
        {
            return;
        }

        var descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion.Trim();
        Guardando = true;
        AvisoPanel = null;
        try
        {
            if (Editando is { } plan)
            {
                // Sin próximo y con último, la API lo calcula (último + periodicidad); vacía, la descripción vuelve a la de siempre.
                var datos = new DatosActualizacionPlan(descripcion ?? string.Empty, dias, ultima, proxima);
                var actualizado = await servicio.ActualizarPlanAsync(plan.Id, datos, CancellationToken.None);
                Reemplazar(actualizado);
                Exito = $"Plan de {actualizado.PlacaInventario} actualizado. Próximo preventivo: {TextosEquipo.Fecha(actualizado.ProximaFecha)}.";
            }
            else
            {
                var creado = await servicio.CrearPlanAsync(new DatosPlan(EquipoSeleccionado!.Id, descripcion, dias, ultima, proxima), CancellationToken.None);
                Exito = creado.Vencido
                    ? $"Plan de {creado.PlacaInventario} creado. Su próximo preventivo ({TextosEquipo.Fecha(creado.ProximaFecha)}) ya pasó: está vencido."
                    : $"Plan de {creado.PlacaInventario} creado. Próximo preventivo: {TextosEquipo.Fecha(creado.ProximaFecha)}.";
                await ConsultarAsync(1);
            }

            PanelAbierto = false;
            Editando = null;
        }
        catch (ErrorApi error)
        {
            ErrorEquipo = error.Errores.GetValueOrDefault("equipoId") ?? ErrorEquipo;
            ErrorDescripcion = error.Errores.GetValueOrDefault("descripcion");
            ErrorPeriodicidad = error.Errores.GetValueOrDefault("periodicidadDias");
            ErrorUltima = error.Errores.GetValueOrDefault("ultimaEjecucion");
            ErrorProxima = error.Errores.GetValueOrDefault("proximaFecha");
            if (error.Errores.Count == 0)
            {
                AvisoPanel = new Aviso(error.SinConexion ? "Sin conexión" : "No se pudo guardar el plan", error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
            }
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeGuardar() => PanelAbierto && !Guardando;

    /// <summary>Suspender o reactivar; finalizar pide confirmar antes (no se deshace).</summary>
    [RelayCommand(CanExecute = nameof(PuedeCambiarEstado))]
    private async Task CambiarEstadoAsync(CambioEstadoPlan? cambio)
    {
        if (cambio is null || !PuedeGestionar)
        {
            return;
        }

        if (cambio.Estado == EstadosPlan.Finalizado && ConfirmandoFinalizar != cambio.Fila)
        {
            ConfirmandoFinalizar = cambio.Fila;
            return;
        }

        ConfirmandoFinalizar = null;
        Guardando = true;
        try
        {
            var actualizado = await servicio.ActualizarPlanAsync(cambio.Fila.Plan.Id, new DatosActualizacionPlan(Estado: cambio.Estado), CancellationToken.None);
            Reemplazar(actualizado);
            Exito = actualizado.Estado switch
            {
                EstadosPlan.Suspendido => $"Plan de {actualizado.PlacaInventario} suspendido: no generará órdenes ni alertas hasta que lo reactives.",
                EstadosPlan.Finalizado => $"Plan de {actualizado.PlacaInventario} finalizado.",
                _ => $"Plan de {actualizado.PlacaInventario} reactivado. Próximo preventivo: {TextosEquipo.Fecha(actualizado.ProximaFecha)}.",
            };
            if (Editando?.Id == actualizado.Id)
            {
                CerrarPanel();
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : $"No se pudo cambiar el plan de {cambio.Fila.Plan.PlacaInventario}",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeCambiarEstado(CambioEstadoPlan? cambio) => !Guardando;

    [RelayCommand]
    private void CancelarFinalizar() => ConfirmandoFinalizar = null;

    // ---------- Apoyo ----------

    partial void OnBusquedaChanged(string value)
    {
        if (_eligiendo)
        {
            return;
        }

        EquipoSeleccionado = null;
        BuscarEquiposCommand.Execute(null);
    }

    partial void OnPanelAbiertoChanged(bool value) => GuardarCommand.NotifyCanExecuteChanged();

    partial void OnDescripcionChanged(string value) =>
        ErrorDescripcion = value.Trim().Length > MaximoDescripcion ? $"Máximo {MaximoDescripcion} caracteres." : null;

    partial void OnPeriodicidadChanged(string value) => ErrorPeriodicidad = null;

    partial void OnUltimaEjecucionChanged(string value) => ErrorUltima = null;

    partial void OnProximaFechaChanged(string value) => ErrorProxima = null;

    private bool Validar(out int dias, out DateOnly? ultima, out DateOnly? proxima)
    {
        LimpiarErrores();
        ultima = proxima = null;
        var hoy = Hoy();

        if (EsNuevo && EquipoSeleccionado is null)
        {
            ErrorEquipo = SinEquipo;
        }

        if (Descripcion.Trim().Length > MaximoDescripcion)
        {
            ErrorDescripcion = $"Máximo {MaximoDescripcion} caracteres.";
        }

        if (!int.TryParse(Periodicidad.Trim(), NumberStyles.None, Cultura, out dias) || dias is < 1 or > short.MaxValue)
        {
            ErrorPeriodicidad = PeriodicidadInvalida;
        }

        if (!string.IsNullOrWhiteSpace(UltimaEjecucion))
        {
            ultima = FormularioEquipoViewModel.Fecha(UltimaEjecucion);
            ErrorUltima = ultima is null ? FechaInvalida : ultima > hoy ? UltimaFutura : null;
        }

        if (!string.IsNullOrWhiteSpace(ProximaFecha))
        {
            proxima = FormularioEquipoViewModel.Fecha(ProximaFecha);
            if (proxima is null)
            {
                ErrorProxima = FechaInvalida;
            }
            else if (ultima is { } u && proxima <= u)
            {
                ErrorProxima = ProximaAntesDeUltima;
            }
            else if (ultima is null && proxima < hoy && proxima != Editando?.ProximaFecha)
            {
                // Un plan vencido conserva su próxima fecha al editarlo.
                ErrorProxima = ProximaPasada;
            }
        }
        else if (ultima is null && ErrorUltima is null)
        {
            ErrorProxima = SinFechas;
        }

        return (ErrorEquipo ?? ErrorDescripcion ?? ErrorPeriodicidad ?? ErrorUltima ?? ErrorProxima) is null;
    }

    private void LimpiarErrores()
    {
        ErrorEquipo = ErrorDescripcion = ErrorPeriodicidad = ErrorUltima = ErrorProxima = null;
        AvisoPanel = null;
    }

    private void Reemplazar(PlanMantenimiento plan)
    {
        var hoy = Hoy();
        Filas = Filas.Select(f => f.Plan.Id == plan.Id ? new FilaPlan(plan, hoy) : f).ToList();
    }

    private DateOnly Hoy() => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

    private async Task ConsultarAsync(int pagina)
    {
        var estado = string.IsNullOrEmpty(FiltroEstado.Valor) ? null : FiltroEstado.Valor;
        var filtro = new FiltroPlanes(FiltroEquipo?.Id, estado, pagina, TamanoPagina);
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await servicio.ListarPlanesAsync(filtro, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            var hoy = Hoy();
            Filas = resultado.Items.Select(p => new FilaPlan(p, hoy)).ToList();
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
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar los planes",
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
                OnPropertyChanged(nameof(SinPlanes));
            }
        }
    }
}

/// <summary>Fila de UI-015 con los textos del plan; el estado y el vencimiento van con punto y texto (ACC-001).</summary>
public sealed record FilaPlan(PlanMantenimiento Plan, DateOnly Hoy)
{
    public string Equipo => $"{Plan.PlacaInventario} · {Plan.TipoEquipo}";

    public string Ubicacion => Plan.Ubicacion;

    public string Descripcion => Plan.Descripcion;

    public string Periodicidad => Plan.PeriodicidadDias == 1 ? "Cada día" : $"Cada {Plan.PeriodicidadDias:N0} días";

    public string Ultimo => Plan.UltimaEjecucion is { } u ? TextosEquipo.Fecha(u) : "Sin registro";

    public string Proximo => TextosEquipo.Fecha(Plan.ProximaFecha);

    public bool Activo => Plan.Estado == EstadosPlan.Activo;

    /// <summary>Cuánto falta, o hace cuánto venció, solo para los planes activos.</summary>
    public string Vencimiento
    {
        get
        {
            if (!Activo)
            {
                return string.Empty;
            }

            var dias = Plan.ProximaFecha.DayNumber - Hoy.DayNumber;
            return dias switch
            {
                < -1 => $"Vencido hace {-dias} días",
                -1 => "Vencido ayer",
                0 => "Vence hoy",
                1 => "Vence mañana",
                _ => $"En {dias} días",
            };
        }
    }

    public bool Vencido => Activo && Plan.ProximaFecha < Hoy;

    public string Estado => Vencido ? "Vencido" : EstadosPlan.Nombre(Plan.Estado);

    public TonoEstado Tono => Plan.Estado switch
    {
        EstadosPlan.Activo when Vencido => TonoEstado.Critico,
        EstadosPlan.Activo => TonoEstado.Exito,
        EstadosPlan.Suspendido => TonoEstado.Advertencia,
        _ => TonoEstado.Neutro,
    };

    /// <summary>Un plan finalizado ya no cambia (409 PLN-002).</summary>
    public bool Editable => Plan.Estado != EstadosPlan.Finalizado;

    public bool PuedeSuspender => Activo;

    public bool PuedeReactivar => Plan.Estado == EstadosPlan.Suspendido;

    public CambioEstadoPlan Suspender => new(this, EstadosPlan.Suspendido);

    public CambioEstadoPlan Reactivar => new(this, EstadosPlan.Activo);

    public CambioEstadoPlan Finalizar => new(this, EstadosPlan.Finalizado);
}

/// <summary>Parámetro de «Suspender», «Reactivar» y «Finalizar».</summary>
public sealed record CambioEstadoPlan(FilaPlan Fila, string Estado);
