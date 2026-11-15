using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-017 Calibraciones (HU-022; FLU-008), pestaña de UI-015: el historial de calibraciones de la institución, la
/// más reciente primero, con su vencimiento y vigencia en texto. El Técnico (AC-03) registra la calibración con
/// fecha, patrón de referencia, resultado y vencimiento (CA-HU-022-01) y la corrige mientras no esté aprobada;
/// sin vencimiento, la API lo calcula con la periodicidad del equipo. Solo se ofrecen equipos que requieren
/// calibración (CA-HU-022-03, RN-010). El Ingeniero (AC-02) aprueba; AC-01, AC-05 y AC-07 consultan.
/// </summary>
public partial class CalibracionesViewModel(IServicioMantenimiento servicio, IServicioEquipos equipos, SesionActual sesion, TimeProvider tiempo)
    : ObservableObject
{
    public const int TamanoPagina = 20;
    public const int MinimoBusqueda = 2;
    public const int MaximoPatron = 120;
    public const int MaximoCertificado = 80;
    public const string SinEquipo = "Selecciona el equipo calibrado.";
    public const string SinFecha = "Escribe la fecha de la calibración como dd/mm/aaaa.";
    public const string FechaFutura = "La fecha de la calibración no puede ser futura.";
    public const string SinPatron = "Escribe el patrón de referencia usado.";
    public const string FechaInvalida = "Escribe la fecha como dd/mm/aaaa.";
    public const string VencimientoAnterior = "El vencimiento no puede ser anterior a la fecha de la calibración.";

    private const string EstadoBaja = "BAJA";

    private int _consulta;
    private bool _eligiendo;
    private CancellationTokenSource? _cancelacionBusqueda;

    public static IReadOnlyList<OpcionFiltro> OpcionesResultado { get; } =
    [
        new(ResultadosCalibracion.Conforme, "Conforme"),
        new(ResultadosCalibracion.NoConforme, "No conforme"),
    ];

    /// <summary>Solo AC-03 registra y corrige (la API lo valida de nuevo, RNF-003).</summary>
    public bool PuedeRegistrar => sesion.Sesion?.Rol == "AC-03";

    /// <summary>Solo AC-02 aprueba.</summary>
    public bool PuedeAprobar => sesion.Sesion?.Rol == "AC-02";

    public bool SoloConsulta => !PuedeRegistrar && !PuedeAprobar;

    // ---------- Lista ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCalibraciones))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCalibraciones))]
    private Aviso? _aviso;

    [ObservableProperty]
    private string? _exito;

    [ObservableProperty]
    private IReadOnlyList<FilaCalibracion> _filas = [];

    /// <summary>Equipo al que se limitó el historial (al llegar desde una alerta); null = todos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltroEquipo), nameof(TextoFiltroEquipo))]
    private (Guid Id, string? Placa)? _filtroEquipo;

    public bool HayFiltroEquipo => FiltroEquipo is not null;

    public string TextoFiltroEquipo => FiltroEquipo is { } f ? (f.Placa is { } placa ? $"Solo el equipo {placa}" : "Solo un equipo") : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinCalibraciones))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    public bool Cargado { get; private set; }

    public bool SinCalibraciones => Cargado && !Cargando && Aviso is null && Total == 0;

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 calibraciones"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "calibración" : "calibraciones")}";

    /// <summary>Aprobar no se deshace: se pide confirmar.</summary>
    [ObservableProperty]
    private FilaCalibracion? _confirmandoAprobar;

    // ---------- Formulario ----------

    [ObservableProperty]
    private bool _formularioAbierto;

    /// <summary>Calibración en corrección; null con el formulario en «Registrar calibración».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsNueva), nameof(TituloFormulario))]
    private Calibracion? _editando;

    public bool EsNueva => Editando is null;

    public string TituloFormulario => Editando is { } c ? $"Corregir la calibración de {c.PlacaInventario}" : "Registrar calibración";

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
    [NotifyPropertyChangedFor(nameof(HayEquipo), nameof(TextoEquipo), nameof(SinCoincidencias), nameof(VencimientoCalculado))]
    private Equipo? _equipoSeleccionado;

    public bool HayCoincidencias => Coincidencias.Count > 0;

    public bool SinCoincidencias => BusquedaHecha && !HayCoincidencias && EquipoSeleccionado is null;

    public bool HayEquipo => EquipoSeleccionado is not null;

    public string TextoEquipo => EquipoSeleccionado is { } e
        ? $"{e.PlacaInventario} · {e.TipoEquipo} · {e.Ubicacion} · cada {e.PeriodicidadCalibracionMeses} {(e.PeriodicidadCalibracionMeses == 1 ? "mes" : "meses")}"
        : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VencimientoCalculado))]
    private string _fechaCalibracion = string.Empty;

    [ObservableProperty]
    private string _patron = string.Empty;

    [ObservableProperty]
    private OpcionFiltro _resultado = OpcionesResultado[0];

    [ObservableProperty]
    private string _certificado = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VencimientoCalculado))]
    private string _vencimiento = string.Empty;

    [ObservableProperty]
    private string? _errorEquipo;

    [ObservableProperty]
    private string? _errorFecha;

    [ObservableProperty]
    private string? _errorPatron;

    [ObservableProperty]
    private string? _errorCertificado;

    [ObservableProperty]
    private string? _errorVencimiento;

    [ObservableProperty]
    private Aviso? _avisoFormulario;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand), nameof(AprobarCommand))]
    private bool _guardando;

    /// <summary>Sin vencimiento indicado: lo que calculará la API (fecha + la periodicidad del equipo, RN-010).</summary>
    public string VencimientoCalculado
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Vencimiento) || FormularioEquipoViewModel.Fecha(FechaCalibracion) is not { } fecha)
            {
                return string.Empty;
            }

            var meses = EquipoSeleccionado?.PeriodicidadCalibracionMeses;
            if (meses is not { } m)
            {
                return Editando is null ? string.Empty : "Vence: se recalcula con la periodicidad del equipo si cambias la fecha.";
            }

            var vence = fecha.AddMonths(m);
            return vence < Hoy()
                ? $"Vence el {TextosEquipo.Fecha(vence)}. Ya pasó: la calibración quedará vencida."
                : $"Vence el {TextosEquipo.Fecha(vence)}.";
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

    /// <summary>NAV-011: la alerta de calibración muestra el historial de ese equipo.</summary>
    public Task MostrarEquipoAsync(Guid equipoId)
    {
        FiltroEquipo = (equipoId, null);
        return ConsultarAsync(1);
    }

    /// <summary>«Aprobar» (AC-02); pide confirmar porque aprobada ya no se corrige.</summary>
    [RelayCommand(CanExecute = nameof(PuedeAprobarFila))]
    private async Task AprobarAsync(FilaCalibracion? fila)
    {
        if (fila is null || !fila.PuedeAprobar)
        {
            return;
        }

        if (ConfirmandoAprobar != fila)
        {
            ConfirmandoAprobar = fila;
            return;
        }

        ConfirmandoAprobar = null;
        Guardando = true;
        try
        {
            var aprobada = await servicio.AprobarCalibracionAsync(fila.Calibracion.Id, CancellationToken.None);
            Reemplazar(aprobada);
            Exito = $"Calibración de {aprobada.PlacaInventario} del {TextosEquipo.Fecha(aprobada.FechaCalibracion)} aprobada.";
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : $"No se pudo aprobar la calibración de {fila.Calibracion.PlacaInventario}",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeAprobarFila(FilaCalibracion? fila) => !Guardando;

    [RelayCommand]
    private void CancelarAprobar() => ConfirmandoAprobar = null;

    // ---------- Formulario ----------

    [RelayCommand]
    private void NuevaCalibracion()
    {
        if (!PuedeRegistrar)
        {
            return;
        }

        Editando = null;
        _eligiendo = true;
        Busqueda = string.Empty;
        _eligiendo = false;
        (EquipoSeleccionado, Coincidencias, BusquedaHecha) = (null, [], false);
        FechaCalibracion = TextosEquipo.Fecha(Hoy());
        Patron = Certificado = Vencimiento = string.Empty;
        Resultado = OpcionesResultado[0];
        LimpiarErrores();
        Exito = null;
        FormularioAbierto = true;
    }

    [RelayCommand]
    private void AbrirCorreccion(FilaCalibracion? fila)
    {
        if (fila is null || !fila.PuedeCorregir)
        {
            return;
        }

        var c = fila.Calibracion;
        Editando = c;
        EquipoSeleccionado = null;
        FechaCalibracion = TextosEquipo.Fecha(c.FechaCalibracion);
        Patron = c.PatronReferencia;
        Resultado = OpcionesResultado.FirstOrDefault(o => o.Valor == c.Resultado) ?? OpcionesResultado[0];
        Certificado = c.NumeroCertificado ?? string.Empty;
        Vencimiento = TextosEquipo.Fecha(c.FechaVencimiento);
        LimpiarErrores();
        Exito = null;
        FormularioAbierto = true;
    }

    [RelayCommand]
    private void CerrarFormulario()
    {
        FormularioAbierto = false;
        Editando = null;
        AvisoFormulario = null;
    }

    /// <summary>CMP-UI-003: busca por placa y por serie solo entre los equipos que requieren calibración y no están de baja (RN-010).</summary>
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
            var porPlaca = await equipos.ListarAsync(new FiltroEquipos(Placa: texto, Calibracion: true, Tamano: 8), cancelacion.Token);
            var porSerie = await equipos.ListarAsync(new FiltroEquipos(Serie: texto, Calibracion: true, Tamano: 8), cancelacion.Token);
            if (!cancelacion.IsCancellationRequested)
            {
                Coincidencias = porPlaca.Items.Concat(porSerie.Items)
                    .Where(e => e.Estado != EstadoBaja && e.RequiereCalibracion)
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
            AvisoFormulario = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos buscar el equipo", error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
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

    /// <summary>«Guardar calibración» (CA-HU-022-01). La API valida de nuevo y responde junto al campo.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (!Validar(out var fecha, out var vencimiento))
        {
            return;
        }

        var certificado = Certificado.Trim();
        Guardando = true;
        AvisoFormulario = null;
        try
        {
            Calibracion guardada;
            if (Editando is { } actual)
            {
                // Si cambia la fecha y se vacía el vencimiento, la API lo recalcula con la periodicidad del equipo.
                var datos = new DatosCorreccionCalibracion(fecha, Patron.Trim(), Resultado.Valor, certificado, vencimiento);
                guardada = await servicio.CorregirCalibracionAsync(actual.Id, datos, CancellationToken.None);
                Reemplazar(guardada);
                Exito = $"Calibración de {guardada.PlacaInventario} corregida. Vence el {TextosEquipo.Fecha(guardada.FechaVencimiento)}.";
            }
            else
            {
                var datos = new DatosCalibracion(EquipoSeleccionado!.Id, fecha, Patron.Trim(), Resultado.Valor, certificado.Length == 0 ? null : certificado, vencimiento);
                guardada = await servicio.RegistrarCalibracionAsync(datos, CancellationToken.None);
                Exito = guardada.Vigente
                    ? $"Calibración de {guardada.PlacaInventario} registrada. Vence el {TextosEquipo.Fecha(guardada.FechaVencimiento)}."
                    : $"Calibración de {guardada.PlacaInventario} registrada; su vencimiento ({TextosEquipo.Fecha(guardada.FechaVencimiento)}) ya pasó.";
                await ConsultarAsync(1);
            }

            FormularioAbierto = false;
            Editando = null;
        }
        catch (ErrorApi error)
        {
            ErrorEquipo = error.Errores.GetValueOrDefault("equipoId") ?? ErrorEquipo;
            ErrorFecha = error.Errores.GetValueOrDefault("fechaCalibracion");
            ErrorPatron = error.Errores.GetValueOrDefault("patronReferencia");
            ErrorCertificado = error.Errores.GetValueOrDefault("numeroCertificado");
            ErrorVencimiento = error.Errores.GetValueOrDefault("fechaVencimiento");
            if (error.Errores.Count == 0)
            {
                AvisoFormulario = new Aviso(error.SinConexion ? "Sin conexión" : "No se pudo guardar la calibración", error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
            }
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeGuardar() => FormularioAbierto && !Guardando;

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

    partial void OnFormularioAbiertoChanged(bool value) => GuardarCommand.NotifyCanExecuteChanged();

    partial void OnFechaCalibracionChanged(string value) => ErrorFecha = null;

    partial void OnPatronChanged(string value) =>
        ErrorPatron = value.Trim().Length > MaximoPatron ? $"Máximo {MaximoPatron} caracteres." : null;

    partial void OnCertificadoChanged(string value) =>
        ErrorCertificado = value.Trim().Length > MaximoCertificado ? $"Máximo {MaximoCertificado} caracteres." : null;

    partial void OnVencimientoChanged(string value) => ErrorVencimiento = null;

    private bool Validar(out DateOnly fecha, out DateOnly? vencimiento)
    {
        LimpiarErrores();
        vencimiento = null;
        fecha = default;

        if (EsNueva && EquipoSeleccionado is null)
        {
            ErrorEquipo = SinEquipo;
        }

        if (FormularioEquipoViewModel.Fecha(FechaCalibracion) is not { } leida)
        {
            ErrorFecha = SinFecha;
        }
        else if (leida > Hoy())
        {
            ErrorFecha = FechaFutura;
        }
        else
        {
            fecha = leida;
        }

        var patron = Patron.Trim();
        ErrorPatron = patron.Length == 0 ? SinPatron : patron.Length > MaximoPatron ? $"Máximo {MaximoPatron} caracteres." : null;
        ErrorCertificado = Certificado.Trim().Length > MaximoCertificado ? $"Máximo {MaximoCertificado} caracteres." : null;

        if (!string.IsNullOrWhiteSpace(Vencimiento))
        {
            vencimiento = FormularioEquipoViewModel.Fecha(Vencimiento);
            ErrorVencimiento = vencimiento is null ? FechaInvalida : ErrorFecha is null && vencimiento < fecha ? VencimientoAnterior : null;
        }

        return (ErrorEquipo ?? ErrorFecha ?? ErrorPatron ?? ErrorCertificado ?? ErrorVencimiento) is null;
    }

    private void LimpiarErrores()
    {
        ErrorEquipo = ErrorFecha = ErrorPatron = ErrorCertificado = ErrorVencimiento = null;
        AvisoFormulario = null;
    }

    private FilaCalibracion Fila(Calibracion calibracion) => new(calibracion, Hoy(), PuedeRegistrar, PuedeAprobar);

    private void Reemplazar(Calibracion calibracion) =>
        Filas = Filas.Select(f => f.Calibracion.Id == calibracion.Id ? Fila(calibracion) : f).ToList();

    private DateOnly Hoy() => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

    private async Task ConsultarAsync(int pagina)
    {
        var filtro = new FiltroCalibraciones(FiltroEquipo?.Id, pagina, TamanoPagina);
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await servicio.ListarCalibracionesAsync(filtro, CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Filas = resultado.Items.Select(Fila).ToList();
            Total = resultado.Total;
            Pagina = pagina;
            Aviso = null;
            if (FiltroEquipo is { Placa: null } sinPlaca && resultado.Items.Count > 0)
            {
                FiltroEquipo = (sinPlaca.Id, resultado.Items[0].PlacaInventario);
            }
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
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar las calibraciones",
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
                OnPropertyChanged(nameof(SinCalibraciones));
            }
        }
    }
}

/// <summary>Fila de UI-017 con los textos de la calibración; la vigencia va con punto y texto (ACC-001).</summary>
public sealed record FilaCalibracion(Calibracion Calibracion, DateOnly Hoy, bool RolCorrige, bool RolAprueba)
{
    /// <summary>Desde cuántos días antes del vencimiento la vigencia se marca en advertencia.</summary>
    public const int DiasAviso = 30;

    public string Equipo => $"{Calibracion.PlacaInventario} · {Calibracion.TipoEquipo}";

    public string Ubicacion => Calibracion.Ubicacion;

    public string Fecha => TextosEquipo.Fecha(Calibracion.FechaCalibracion);

    public string Patron => Calibracion.PatronReferencia;

    public string Certificado => Calibracion.NumeroCertificado is { } numero ? $"Certificado {numero}" : "Sin certificado";

    public string Resultado => ResultadosCalibracion.Nombre(Calibracion.Resultado);

    public string Vence => TextosEquipo.Fecha(Calibracion.FechaVencimiento);

    public string Vigencia
    {
        get
        {
            var dias = Calibracion.FechaVencimiento.DayNumber - Hoy.DayNumber;
            return dias switch
            {
                < -1 => $"Vencida hace {-dias} días",
                -1 => "Vencida ayer",
                0 => "Vence hoy",
                1 => "Vence mañana",
                _ => $"Vigente · {dias} días",
            };
        }
    }

    public TonoEstado Tono => (Calibracion.FechaVencimiento.DayNumber - Hoy.DayNumber) switch
    {
        < 0 => TonoEstado.Critico,
        <= DiasAviso => TonoEstado.Advertencia,
        _ => TonoEstado.Exito,
    };

    public string Aprobacion => Calibracion.Aprobada ? $"Aprobada por {Calibracion.AprobadoPor}" : "Pendiente de aprobación";

    public string Registro => $"Registró {Calibracion.RegistradoPor}";

    /// <summary>El Técnico corrige mientras no esté aprobada (409 CAL-003).</summary>
    public bool PuedeCorregir => RolCorrige && !Calibracion.Aprobada;

    public bool PuedeAprobar => RolAprueba && !Calibracion.Aprobada;
}
