using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-006 Hoja de vida del equipo (HU-002; RF-030, RN-010). Pestañas «Resumen», «Información
/// técnica», «Mantenimientos» (plan, órdenes y calibraciones), «Historial» (línea de tiempo, HU-023) y
/// «Documentos» (HU-015). El encabezado muestra la foto del equipo (RF-031) o el ícono del tipo, según Preferencias.
/// AC-02 edita (UI-007), da de baja y revierte la baja (HU-014) desde aquí; la API lo valida.
/// AC-02 y AC-04 reportan una falla del equipo (UI-009) y los roles de la política de la API ven la
/// hoja de vida en PDF (RF-027) con su vista previa y «Guardar».
/// </summary>
public partial class DetalleEquipoViewModel(
    IServicioEquipos servicio,
    TimeProvider tiempo,
    Guid equipoId,
    string placa,
    Action volver,
    bool puedeEditar = false,
    Func<Task>? alCambiarEstado = null,
    IServicioMantenimiento? mantenimiento = null,
    IServicioOrdenes? ordenes = null,
    string? rol = null,
    IServicioDocumentosEquipo? documentos = null,
    IDialogoArchivos? dialogo = null,
    IServicioFotoEquipo? fotos = null,
    IReductorFoto? reductor = null,
    bool verFotos = true,
    IServicioReportes? reportes = null,
    Action<Guid, string>? reportarFalla = null) : ObservableObject
{
    /// <summary>Roles que reportan fallas (UI-009 en su menú).</summary>
    private static readonly HashSet<string> RolesReportan = ["AC-02", "AC-04"];

    /// <summary>Roles de la política «HojaVida» de la API (GET /equipos/{id}/hoja-vida.pdf).</summary>
    private static readonly HashSet<string> RolesHojaVidaPdf = ["AC-01", "AC-02", "AC-05", "AC-07"];

    public string Placa { get; } = placa;

    /// <summary>Solo AC-02 ve «Editar» (la API también lo valida, RNF-003); un equipo de baja no se edita.</summary>
    public bool PuedeEditar => puedeEditar && HayEquipo && !EstaDeBaja;

    /// <summary>HU-014: «Dar de baja» en el encabezado (solo AC-02).</summary>
    public bool PuedeDarDeBaja => PuedeEditar;

    /// <summary>HU-014: «Revertir» en el aviso de baja (solo AC-02; RN-009 lo decide la API).</summary>
    public bool PuedeRevertir => puedeEditar && EstaDeBaja;

    public bool EstaDeBaja => Equipo?.Estado == EstadosEquipo.Baja;

    /// <summary>«Reportar falla»: AC-02 y AC-04, con el equipo activo (un equipo de baja no recibe reportes).</summary>
    public bool PuedeReportarFalla => reportarFalla is not null && rol is not null && RolesReportan.Contains(rol) && HayEquipo && !EstaDeBaja;

    /// <summary>«Hoja de vida PDF»: los roles de la política de la API, también con el equipo de baja (se conserva para auditoría).</summary>
    public bool PuedeHojaVidaPdf => reportes is not null && dialogo is not null && rol is not null && RolesHojaVidaPdf.Contains(rol) && HayEquipo;

    /// <summary>El menú «…» aparece si tiene al menos una acción: ver o cambiar la foto, dar de baja o revertirla.</summary>
    public bool HayMasAcciones => PuedeVerFoto || PuedeCambiarFoto || PuedeDarDeBaja || PuedeRevertir;

    [RelayCommand]
    private void ReportarFalla()
    {
        if (PuedeReportarFalla)
        {
            reportarFalla!(equipoId, Placa);
        }
    }

    /// <summary>Diálogo de la hoja de vida en PDF abierto (vista previa con los datos de la hoja de vida).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarHojaVidaPdfCommand))]
    private bool _vistaPreviaPdf;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarHojaVidaPdfCommand))]
    private bool _generandoPdf;

    /// <summary>Error al generar o guardar el PDF; se muestra dentro del diálogo.</summary>
    [ObservableProperty]
    private Aviso? _avisoPdf;

    public string NombrePdf => $"HV_{Placa}.pdf";

    [RelayCommand]
    private void AbrirHojaVidaPdf()
    {
        if (PuedeHojaVidaPdf)
        {
            AvisoPdf = null;
            Confirmacion = null;
            VistaPreviaPdf = true;
        }
    }

    [RelayCommand]
    private void CerrarHojaVidaPdf() => VistaPreviaPdf = false;

    private bool PuedeGuardarPdf => VistaPreviaPdf && !GenerandoPdf;

    /// <summary>Pide el PDF a la API (RF-027, queda en la bitácora) y lo guarda donde elija el usuario.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarPdf))]
    private async Task GuardarHojaVidaPdfAsync()
    {
        GenerandoPdf = true;
        AvisoPdf = null;
        try
        {
            var archivo = await reportes!.HojaVidaAsync(equipoId, CancellationToken.None);
            var ruta = dialogo!.ElegirDestino(archivo.NombreArchivo);
            if (ruta is null)
            {
                return;
            }

            dialogo.Guardar(ruta, archivo.Contenido);
            VistaPreviaPdf = false;
            Confirmacion = new Aviso("Hoja de vida guardada", ruta, null, EsError: false);
        }
        catch (ErrorApi error)
        {
            AvisoPdf = new Aviso(error.SinConexion ? "Sin conexión" : "No pudimos generar la hoja de vida", error.SinConexion ? Mensajes.Msg011 : error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
        }
        catch (System.IO.IOException error)
        {
            AvisoPdf = new Aviso("No pudimos guardar el archivo", error.Message, null);
        }
        catch (UnauthorizedAccessException)
        {
            AvisoPdf = new Aviso("No pudimos guardar el archivo", "No tienes permiso para escribir en esa carpeta. Elige otra.", null);
        }
        finally
        {
            GenerandoPdf = false;
        }
    }

    /// <summary>Diálogo de baja o de reversión abierto; null = cerrado.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarBajaCommand))]
    private BajaEquipoViewModel? _baja;

    /// <summary>Confirmación tras guardar cambios desde el formulario UI-007.</summary>
    [ObservableProperty]
    private Aviso? _confirmacion;

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HayEquipo), nameof(PuedeEditar), nameof(PuedeDarDeBaja), nameof(PuedeRevertir), nameof(EstaDeBaja), nameof(Titulo), nameof(Subtitulo), nameof(Estado), nameof(Tono), nameof(AvisoRegulatorio), nameof(AvisoBaja),
        nameof(ProximoMantenimiento), nameof(DetalleMantenimiento), nameof(MantenimientoVencido), nameof(Calibracion), nameof(DetalleCalibracion),
        nameof(Autorizacion), nameof(DetalleAutorizacion), nameof(AutorizacionEnAlerta), nameof(DatosActivo), nameof(InformacionTecnica),
        nameof(InformacionRegulatoria), nameof(PuedeCambiarFoto), nameof(IconoTipo), nameof(PuedeReportarFalla), nameof(PuedeHojaVidaPdf), nameof(HayMasAcciones))]
    private Equipo? _equipo;

    private enum Pestana
    {
        Resumen,
        Tecnica,
        Mantenimientos,
        Historial,
        Documentos,
    }

    private Pestana _pestana = Pestana.Resumen;

    public bool EnResumen
    {
        get => _pestana == Pestana.Resumen;
        set => Elegir(Pestana.Resumen, value);
    }

    public bool EnTecnica
    {
        get => _pestana == Pestana.Tecnica;
        set => Elegir(Pestana.Tecnica, value);
    }

    /// <summary>La pestaña «Mantenimientos» solo aparece para los roles que consultan los planes (API-PLAN).</summary>
    public bool MuestraMantenimientos => mantenimiento is not null && ordenes is not null && MantenimientosEquipoViewModel.PuedeVer(rol);

    /// <summary>«Mantenimientos»: plan, órdenes y calibraciones se piden la primera vez que se abre la pestaña.</summary>
    public bool EnMantenimientos
    {
        get => _pestana == Pestana.Mantenimientos;
        set => Elegir(Pestana.Mantenimientos, value);
    }

    /// <summary>Contenido de la pestaña «Mantenimientos»; null hasta abrirla.</summary>
    [ObservableProperty]
    private MantenimientosEquipoViewModel? _mantenimientos;

    /// <summary>«Historial»: la línea de tiempo se pide la primera vez que se abre la pestaña.</summary>
    public bool EnHistorial
    {
        get => _pestana == Pestana.Historial;
        set => Elegir(Pestana.Historial, value);
    }

    /// <summary>«Documentos» (HU-015): se cargan la primera vez que se abre la pestaña.</summary>
    public bool EnDocumentos
    {
        get => _pestana == Pestana.Documentos;
        set => Elegir(Pestana.Documentos, value);
    }

    /// <summary>La pestaña existe si el cliente tiene el servicio de documentos.</summary>
    public bool TieneDocumentos => documentos is not null && dialogo is not null;

    /// <summary>ViewModel de la pestaña Documentos; null hasta abrirla.</summary>
    [ObservableProperty]
    private DocumentosEquipoViewModel? _documentos;

    private void Elegir(Pestana pestana, bool elegida)
    {
        if (!elegida || _pestana == pestana)
        {
            return;
        }

        _pestana = pestana;
        OnPropertyChanged(nameof(EnResumen));
        OnPropertyChanged(nameof(EnTecnica));
        OnPropertyChanged(nameof(EnHistorial));
        OnPropertyChanged(nameof(EnMantenimientos));
        OnPropertyChanged(nameof(EnDocumentos));
        if (pestana == Pestana.Historial && Eventos is null && !CargandoHistorial)
        {
            _ = CargarHistorialCommand.ExecuteAsync(null);
        }

        if (pestana == Pestana.Documentos && Documentos is null && documentos is not null && dialogo is not null)
        {
            // Solo AC-02 agrega y desactiva, y no en un equipo de baja (la API también lo valida).
            Documentos = new DocumentosEquipoViewModel(documentos, dialogo, equipoId, Placa, PuedeEditar);
            _ = Documentos.CargarCommand.ExecuteAsync(null);
        }

        if (pestana == Pestana.Mantenimientos && Mantenimientos is null)
        {
            AbrirMantenimientos();
        }
    }

    private void AbrirMantenimientos()
    {
        if (MuestraMantenimientos && Equipo is { } equipo)
        {
            Mantenimientos = new MantenimientosEquipoViewModel(mantenimiento!, ordenes!, tiempo, equipoId, equipo.RequiereCalibracion, rol);
            _ = Mantenimientos.CargarCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Si el equipo cambió (edición, baja), la pestaña «Mantenimientos» se vuelve a consultar.</summary>
    partial void OnEquipoChanged(Equipo? oldValue, Equipo? newValue)
    {
        TieneFoto = newValue?.TieneFoto == true;
        if (!TieneFoto)
        {
            Miniatura = null;
            _vista = null;
        }

        _ = CargarMiniaturaAsync();
        if (Mantenimientos is null)
        {
            return;
        }

        Mantenimientos = null;
        if (EnMantenimientos)
        {
            AbrirMantenimientos();
        }
    }

    // ---------- Historial: línea de tiempo (HU-023, CMP-UI-014) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEventos))]
    private bool _cargandoHistorial;

    [ObservableProperty]
    private Aviso? _avisoHistorial;

    /// <summary>Los eventos del más antiguo al más reciente (orden cronológico, CA-HU-023-01); null hasta cargarlos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEventos))]
    private IReadOnlyList<FilaEvento>? _eventos;

    /// <summary>MongoDB no respondió: se ve lo demás y se avisa que faltan las intervenciones.</summary>
    [ObservableProperty]
    private Aviso? _historialIncompleto;

    public bool SinEventos => !CargandoHistorial && Eventos is { Count: 0 };

    [RelayCommand]
    private async Task CargarHistorialAsync()
    {
        CargandoHistorial = true;
        try
        {
            var linea = await servicio.LineaTiempoAsync(equipoId, CancellationToken.None);
            Eventos = linea.Eventos.Select(e => new FilaEvento(e)).ToList();
            HistorialIncompleto = linea.IntervencionesCompletas
                ? null
                : new Aviso("Faltan las intervenciones", "El repositorio de documentos no respondió; el resto del historial está completo. Intenta de nuevo en unos minutos.", "MSG-011", EsError: false, PuedeReintentar: true);
            AvisoHistorial = null;
        }
        catch (ErrorApi error)
        {
            AvisoHistorial = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos cargar el historial",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            CargandoHistorial = false;
        }
    }

    public bool HayEquipo => Equipo is not null;

    private DateOnly Hoy => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

    // ---------- Encabezado ----------

    public string Titulo => Equipo?.TipoEquipo ?? Placa;

    /// <summary>E-10: ícono del tipo de equipo cuando no hay foto o la preferencia es el ícono.</summary>
    public string IconoTipo => IconosTipoEquipo.Clave(Equipo?.TipoEquipo);

    public string Subtitulo => Equipo is not { } e
        ? string.Empty
        : string.Join(" · ", new[] { e.ClaseRiesgo is null ? null : $"Clase {e.ClaseRiesgo}", $"{e.Marca} {e.Modelo}", e.Ubicacion }.OfType<string>());

    public string Estado => Equipo?.EstadoNombre ?? string.Empty;

    public TonoEstado Tono => Equipo is null ? TonoEstado.Neutro : TextosEquipo.Tono(Equipo.Estado);

    /// <summary>RN-010: aviso (no bloqueo) si la autorización venció, está por vencer o falta. No aplica a equipos de baja.</summary>
    public Aviso? AvisoRegulatorio
    {
        get
        {
            if (Equipo is not { FechaBaja: null } e)
            {
                return null;
            }

            var autorizacion = TextosEquipo.Autorizacion(e.TipoAutorizacion).ToLower(TextosEquipo.Cultura);
            return e.AvisoRegistro switch
            {
                AvisosRegistro.SinDato => new Aviso(
                    "Falta la autorización sanitaria",
                    "Registra el número de registro sanitario o permiso de comercialización INVIMA del equipo.",
                    "RN-010"),
                AvisosRegistro.Vencido => new Aviso(
                    "Autorización INVIMA vencida",
                    $"El {autorizacion} {e.RegistroInvima} venció el {TextosEquipo.Fecha(e.VenceRegistro)}. Verifica su renovación con el titular o el proveedor y actualiza el dato en la hoja de vida.",
                    "RN-010"),
                AvisosRegistro.PorVencer => new Aviso(
                    "Autorización INVIMA por vencer",
                    $"El {autorizacion} {e.RegistroInvima} vence el {TextosEquipo.Fecha(e.VenceRegistro)} ({DiasHasta(e.VenceRegistro)}). Verifica su renovación a tiempo.",
                    "RN-010",
                    EsError: false),
                _ => null,
            };
        }
    }

    public Aviso? AvisoBaja => Equipo?.FechaBaja is { } baja
        ? new Aviso(
            $"Equipo dado de baja el {baja.ToLocalTime().ToString("dd/MM/yyyy", TextosEquipo.Cultura)}",
            "Ya no cuenta en el inventario activo ni en el límite del plan; su hoja de vida se conserva para auditoría.",
            "RF-014",
            EsError: false)
        : null;

    // ---------- Resumen: indicadores ----------

    public string ProximoMantenimiento => Equipo?.Preventivo is { } plan ? TextosEquipo.Fecha(plan.ProximaFecha) : "Sin plan";

    public string DetalleMantenimiento => Equipo?.Preventivo is not { } plan
        ? "Sin plan preventivo"
        : plan.Vencido ? $"Vencido · {TextosEquipo.Periodicidad(plan.PeriodicidadDias).ToLower(TextosEquipo.Cultura)}" : $"{DiasHasta(plan.ProximaFecha)} · {TextosEquipo.Periodicidad(plan.PeriodicidadDias).ToLower(TextosEquipo.Cultura)}";

    public bool MantenimientoVencido => Equipo?.Preventivo?.Vencido == true;

    public string Calibracion => Equipo is null ? string.Empty : TextosEquipo.Calibracion(Equipo);

    public string DetalleCalibracion => Equipo?.RequiereCalibracion == false ? "No requiere calibración (RN-010)" : "Periodicidad de calibración";

    public string Autorizacion => Equipo?.AvisoRegistro switch
    {
        AvisosRegistro.Vencido => "Vencida",
        AvisosRegistro.PorVencer => "Por vencer",
        AvisosRegistro.Vigente => "Vigente",
        _ => "Sin dato",
    };

    public string DetalleAutorizacion => Equipo?.VenceRegistro is { } vence ? $"Hasta {TextosEquipo.Fecha(vence)}" : "Sin vigencia registrada";

    public bool AutorizacionEnAlerta => Equipo?.AvisoRegistro is AvisosRegistro.Vencido or AvisosRegistro.SinDato;

    // ---------- Datos ----------

    public IReadOnlyList<Dato> DatosActivo => Equipo is not { } e ? [] :
    [
        new("Código de activo", e.PlacaInventario, true),
        new("Número de serie", e.NumeroSerie, true),
        new("Tipo de equipo", e.TipoEquipo),
        new("Fabricante", e.Marca),
        new("Modelo", e.Modelo),
        new("Ubicación", e.Ubicacion),
        new("Registro sanitario INVIMA", e.RegistroInvima ?? TextosEquipo.SinDato, true),
        new("Clase de riesgo", TextosEquipo.Clase(e.ClaseRiesgo)),
        new("Calibración", e.RequiereCalibracion ? TextosEquipo.Calibracion(e) : "No requiere"),
    ];

    public IReadOnlyList<Dato> InformacionTecnica => Equipo is not { } e ? [] :
    [
        new("Tipo", e.TipoEquipo),
        new("Marca y modelo", $"{e.Marca} {e.Modelo}"),
        new("Proveedor", e.Proveedor ?? TextosEquipo.SinDato),
        new("Fecha de adquisición", TextosEquipo.Fecha(e.FechaAdquisicion)),
        new("Mantenimiento preventivo", e.Preventivo is { } p
            ? $"{TextosEquipo.Periodicidad(p.PeriodicidadDias)} · próximo {TextosEquipo.Fecha(p.ProximaFecha)}"
            : "Sin plan preventivo"),
        new("Último preventivo", e.Preventivo?.UltimaEjecucion is { } u ? TextosEquipo.Fecha(u) : "No se conoce"),
    ];

    public IReadOnlyList<Dato> InformacionRegulatoria => Equipo is not { } e ? [] :
    [
        new("Autorización INVIMA", TextosEquipo.Autorizacion(e.TipoAutorizacion)),
        new("Número", e.RegistroInvima ?? TextosEquipo.SinDato, true),
        new("Vigente hasta", $"{TextosEquipo.Fecha(e.VenceRegistro)} · {Autorizacion.ToLower(TextosEquipo.Cultura)}"),
        new("Clase de riesgo", TextosEquipo.Clase(e.ClaseRiesgo)),
        new("Requiere calibración", e.RequiereCalibracion ? $"Sí · {TextosEquipo.Calibracion(e).ToLower(TextosEquipo.Cultura)}" : "No"),
        new("Fuente de la clasificación", "Decreto 4725 de 2005"),
    ];

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Equipo = await servicio.ObtenerAsync(equipoId, CancellationToken.None);
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir la hoja de vida",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private void AbrirBaja() => AbrirDialogo(esReversion: false);

    [RelayCommand]
    private void AbrirReversion() => AbrirDialogo(esReversion: true);

    [RelayCommand]
    private void CancelarBaja() => Baja = null;

    /// <summary>
    /// Envía la baja (BAJA) o la reversión (OPERATIVO, RN-009). Si la API la rechaza, el diálogo
    /// sigue abierto con el error; si la acepta, se cierra y la hoja de vida muestra el equipo nuevo.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmarBajaAsync()
    {
        if (Baja is not { } dialogo || Equipo is not { } equipo || dialogo.Enviando || !dialogo.Validar())
        {
            return;
        }

        dialogo.Enviando = true;
        try
        {
            var estado = dialogo.EsReversion ? EstadosEquipo.Operativo : EstadosEquipo.Baja;
            Equipo = await servicio.CambiarEstadoAsync(equipo.Id, estado, dialogo.Motivo, CancellationToken.None);
            Baja = null;
            Confirmacion = dialogo.EsReversion
                ? new Aviso($"Baja de {Placa} revertida", "El equipo volvió al inventario activo como «Operativo». El cambio quedó en su historial.", null, EsError: false)
                : new Aviso($"{Placa} dado de baja", "Salió del inventario activo y de los indicadores. Su hoja de vida y su historial se conservan.", null, EsError: false);
            if (alCambiarEstado is not null)
            {
                await alCambiarEstado();
            }
        }
        catch (ErrorApi error)
        {
            dialogo.MostrarError(error);
        }
        finally
        {
            dialogo.Enviando = false;
        }
    }

    private void AbrirDialogo(bool esReversion)
    {
        if (Equipo is { } equipo && (esReversion ? PuedeRevertir : PuedeDarDeBaja))
        {
            Confirmacion = null;
            Baja = new BajaEquipoViewModel(Placa, equipo.TipoEquipo, esReversion);
        }
    }

    // ---------- Fotografía del equipo (RF-031) ----------

    private byte[]? _vista;

    /// <summary>El equipo tiene foto guardada (aunque el usuario prefiera ver el ícono).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeVerFoto), nameof(TextoBotonFoto), nameof(HayMasAcciones))]
    private bool _tieneFoto;

    public string TextoBotonFoto => TieneFoto ? "Cambiar la fotografía" : "Agregar una fotografía";

    /// <summary>Miniatura de 128 px para el encabezado; null = ícono del tipo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraFoto))]
    private byte[]? _miniatura;

    /// <summary>Preferencias «Fotografía» y foto disponible; si no, el ícono del tipo.</summary>
    public bool MuestraFoto => verFotos && Miniatura is not null;

    /// <summary>«Ver fotografía» aunque la preferencia sea el ícono.</summary>
    public bool PuedeVerFoto => TieneFoto && fotos is not null;

    /// <summary>Botón de la cámara: solo AC-02 y con el equipo activo (la API también lo valida).</summary>
    public bool PuedeCambiarFoto => PuedeEditar && fotos is not null && reductor is not null && dialogo is not null;

    /// <summary>Diálogo para agregar, cambiar o quitar la foto; null = cerrado.</summary>
    [ObservableProperty]
    private FotoEquipoViewModel? _dialogoFoto;

    /// <summary>Vista de 512 px ampliada; null = cerrada.</summary>
    [ObservableProperty]
    private byte[]? _fotoAmpliada;

    private async Task CargarMiniaturaAsync()
    {
        if (!verFotos || fotos is null || !TieneFoto || Miniatura is not null)
        {
            return;
        }

        Miniatura = await fotos.DescargarAsync(equipoId, ReductorFotoWpf.LadoMiniatura, CancellationToken.None);
    }

    private async Task<byte[]?> VistaAsync()
    {
        if (_vista is null && TieneFoto && fotos is not null)
        {
            _vista = await fotos.DescargarAsync(equipoId, ReductorFotoWpf.LadoMaximoVista, CancellationToken.None);
        }

        return _vista;
    }

    [RelayCommand]
    private async Task VerFotoAsync() => FotoAmpliada = await VistaAsync();

    [RelayCommand]
    private void CerrarFotoAmpliada() => FotoAmpliada = null;

    [RelayCommand]
    private async Task AbrirDialogoFotoAsync()
    {
        if (!PuedeCambiarFoto)
        {
            return;
        }

        var dialogoFoto = new FotoEquipoViewModel(fotos!, reductor!, dialogo!, equipoId, Placa, await VistaAsync());
        dialogoFoto.Cambiada += async (_, cambio) =>
        {
            DialogoFoto = null;
            _vista = cambio.Vista;
            TieneFoto = cambio.Miniatura is not null;
            Miniatura = cambio.Miniatura;
            Confirmacion = cambio.Confirmacion;
            if (alCambiarEstado is not null)
            {
                await alCambiarEstado();
            }
        };
        dialogoFoto.Cancelado += (_, _) => DialogoFoto = null;
        Confirmacion = null;
        DialogoFoto = dialogoFoto;
    }

    /// <summary>Migas: vuelve a «Equipos biomédicos» con los filtros y la página que tenía.</summary>
    [RelayCommand]
    private void Volver() => volver();

    private string DiasHasta(DateOnly? fecha) => TextosEquipo.DiasHasta(fecha, Hoy);
}

/// <summary>Par etiqueta–valor de la hoja de vida; <see cref="Codigo"/> usa letra monoespaciada.</summary>
public sealed record Dato(string Etiqueta, string Valor, bool Codigo = false);

/// <summary>Un evento de la línea de tiempo con su marca (una por tipo, CMP-UI-014).</summary>
public sealed record FilaEvento(EventoLineaTiempo Evento)
{
    public string Fecha => Evento.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", TextosEquipo.Cultura);

    public string Titulo => Evento.Titulo;

    public string? Detalle => Evento.Detalle;

    public string? Responsable => Evento.Responsable;

    public string Tipo => Evento.Tipo switch
    {
        "registro" => "Registro",
        "estado" => "Estado",
        "tiquete" => "Tiquete",
        "orden" => "Orden de trabajo",
        "intervencion" => "Intervención",
        "calibracion" => "Calibración",
        _ => Evento.Tipo,
    };

    public TonoEstado Tono => Evento.Tipo switch
    {
        "tiquete" => TonoEstado.Advertencia,
        "estado" when Evento.Titulo.Contains("Fuera de servicio", StringComparison.OrdinalIgnoreCase) || Evento.Titulo.EndsWith("Baja", StringComparison.OrdinalIgnoreCase) => TonoEstado.Critico,
        "calibracion" when Evento.Titulo.Contains("no conforme", StringComparison.OrdinalIgnoreCase) => TonoEstado.Critico,
        "intervencion" or "calibracion" or "registro" => TonoEstado.Exito,
        _ => TonoEstado.Neutro,
    };
}
