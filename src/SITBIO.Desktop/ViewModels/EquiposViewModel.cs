using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-005 Equipos biomédicos (HU-002; AC-01 a AC-05 y AC-07). Busca por código de activo o serie y
/// filtra por estado, clase de riesgo y calibración en la API (paginación del servidor). Al abrir un
/// equipo muestra su hoja de vida (UI-006) y, al volver, la lista conserva filtros y página (NAV-006).
/// El Ingeniero Biomédico (AC-02) registra y edita con el formulario UI-007; la API vuelve a validar el permiso.
/// Cada fila muestra la miniatura del equipo (RF-031) o el ícono del tipo, según Preferencias.
/// </summary>
public partial class EquiposViewModel(
    IServicioEquipos servicio,
    IServicioCatalogos catalogos,
    SesionActual sesion,
    TimeProvider tiempo,
    IServicioMantenimiento? mantenimiento = null,
    IServicioOrdenes? ordenes = null,
    IServicioDocumentosEquipo? documentos = null,
    IDialogoArchivos? dialogo = null,
    IServicioFotoEquipo? fotos = null,
    IReductorFoto? reductor = null,
    IServicioPreferencias? preferencias = null,
    IServicioReportes? reportes = null) : ObservableObject, IRecibeParametro, ISolicitaNavegacion
{
    /// <summary>UI-009 Reportar falla: «Reportar falla» de la hoja de vida la abre con el equipo ya elegido.</summary>
    public const string PantallaReportarFalla = "UI-009";

    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    public const string IdPantalla = "UI-005";

    public const int TamanoPagina = 20;

    private const string Calibra = "cal-si";
    private const string NoCalibra = "cal-no";

    private int _consulta;
    private bool _aplicandoVarios;

    /// <summary>«Nombre o modelo» busca en el tipo de equipo, el fabricante (marca) y el modelo.</summary>
    public static IReadOnlyList<OpcionFiltro> OpcionesBuscarPor { get; } =
        [new("placa", "Código de activo"), new("serie", "Número de serie"), new("nombre", "Nombre o modelo")];

    /// <summary>Primera opción del filtro «Tipo de equipo»: sin filtrar.</summary>
    public static OpcionFiltro TodoTipo { get; } = new(string.Empty, "Todo tipo de equipo");

    /// <summary>Tipos de equipo del catálogo (API-CAT) para el filtro; si no cargan, solo «Todo tipo de equipo».</summary>
    [ObservableProperty]
    private IReadOnlyList<OpcionFiltro> _opcionesTipo = [TodoTipo];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro? _filtroTipo = TodoTipo;

    private bool _tiposCargados;

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

    public bool HayFiltros => Busqueda.Trim().Length > 0 || FiltroEstado.Valor.Length > 0 || FiltroRiesgo.Valor.Length > 0 || FiltroTipo?.Valor.Length > 0;

    public string TextoBusqueda => BuscarPor.Valor switch
    {
        "serie" => "Buscar por número de serie",
        "nombre" => "Buscar por tipo, fabricante o modelo",
        _ => "Buscar por código de activo",
    };

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

    /// <summary>Solo AC-02 ve «Registrar equipo» y «Editar» (RNF-003: la API también lo valida).</summary>
    public bool PuedeGestionar => sesion.Sesion?.Rol == "AC-02";

    /// <summary>Formulario UI-007 abierto; null = cerrado.</summary>
    [ObservableProperty]
    private FormularioEquipoViewModel? _formulario;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _ocupado;

    /// <summary>Confirmación en la lista tras registrar un equipo.</summary>
    [ObservableProperty]
    private Aviso? _confirmacion;

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
    private Task CargarAsync() => Task.WhenAll(ConsultarAsync(Pagina), CargarTiposAsync());

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
        FiltroTipo = TodoTipo;
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

        var detalle = NuevoDetalle(fila.Equipo.Id, fila.Placa);
        Detalle = detalle;
        return detalle.CargarCommand.ExecuteAsync(null);
    }

    /// <summary>RF-031: «Fotografía» (por omisión) o «Ícono del tipo» en Preferencias.</summary>
    private bool VerFotos => preferencias?.Leer().VerFotosEquipo ?? true;

    private DetalleEquipoViewModel NuevoDetalle(Guid id, string placa) => new(
        servicio, tiempo, id, placa, Volver, PuedeGestionar, () => ConsultarAsync(Pagina), mantenimiento, ordenes, sesion.Sesion?.Rol, documentos, dialogo, fotos, reductor, VerFotos,
        reportes, ReportarFalla);

    private void ReportarFalla(Guid id, string placa) =>
        NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion(PantallaReportarFalla, id.ToString()));

    /// <summary>Dato para abrir la hoja de vida desde otra pantalla (búsqueda global, D-04): «id|placa».</summary>
    public static string ParametroDetalle(Guid id, string placa) => $"{id}|{placa}";

    /// <summary>La búsqueda global abre la hoja de vida (UI-006) del equipo; al volver queda la lista.</summary>
    public void Recibir(string parametro)
    {
        var partes = parametro.Split('|', 2);
        if (!Guid.TryParse(partes[0], out var id))
        {
            return;
        }

        var placa = partes.Length > 1 ? partes[1] : string.Empty;
        var detalle = NuevoDetalle(id, placa);
        Detalle = detalle;
        detalle.CargarCommand.Execute(null);
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    [RelayCommand]
    private Task NuevoEquipoAsync() => AbrirFormularioAsync(null);

    /// <summary>Editar desde la hoja de vida.</summary>
    [RelayCommand]
    private Task EditarAsync(Equipo? equipo) => equipo is null ? Task.CompletedTask : AbrirFormularioAsync(equipo);

    [RelayCommand]
    private void CerrarFormulario() => Formulario = null;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (Formulario is not { } formulario || !formulario.Validar())
        {
            return;
        }

        Ocupado = true;
        try
        {
            if (formulario.EsNuevo)
            {
                var creado = await servicio.CrearAsync(formulario.Datos(), CancellationToken.None);
                Formulario = null;
                Confirmacion = new Aviso(
                    $"Equipo {creado.PlacaInventario} registrado",
                    creado.Preventivo?.Vencido == true
                        ? "Su plan preventivo quedó vencido y aparecerá en las alertas para programarlo."
                        : "Ya está en el inventario con su plan preventivo.",
                    null,
                    EsError: false);
                await ConsultarAsync(Pagina);
            }
            else
            {
                var actualizado = await servicio.ActualizarAsync(formulario.Id!.Value, formulario.Datos(), CancellationToken.None);
                Formulario = null;
                if (Detalle is { } detalle)
                {
                    detalle.Equipo = actualizado;
                    detalle.Confirmacion = new Aviso("Cambios guardados", "La hoja de vida quedó actualizada y el cambio está en la bitácora.", null, EsError: false);
                }

                await ConsultarAsync(Pagina);
            }
        }
        catch (ErrorApi error)
        {
            formulario.MostrarError(error);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private bool PuedeGuardar() => !Ocupado;

    /// <summary>Carga tipos, ubicaciones y proveedores (API-CAT) y abre el formulario.</summary>
    private async Task AbrirFormularioAsync(Equipo? existente)
    {
        Ocupado = true;
        try
        {
            var tipos = catalogos.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None);
            var ubicaciones = catalogos.ListarAsync(TiposCatalogo.Ubicaciones, CancellationToken.None);
            var proveedores = catalogos.ListarAsync(TiposCatalogo.Proveedores, CancellationToken.None);
            await Task.WhenAll(tipos, ubicaciones, proveedores);
            Formulario = new FormularioEquipoViewModel(await tipos, await ubicaciones, await proveedores, Hoy(), existente);
        }
        catch (ErrorApi error)
        {
            var aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir el formulario",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
            if (Detalle is { } detalle)
            {
                detalle.Aviso = aviso;
            }
            else
            {
                Aviso = aviso;
            }
        }
        finally
        {
            Ocupado = false;
        }
    }

    private DateOnly Hoy() => DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);

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

    partial void OnFiltroTipoChanged(OpcionFiltro? value)
    {
        // Al cambiar la lista de opciones, el ComboBox pone null un momento; eso no es un filtro nuevo.
        if (value is not null)
        {
            AlCambiarFiltro();
        }
    }

    /// <summary>Carga una vez los tipos de equipo para el filtro; conserva el tipo elegido.</summary>
    private async Task CargarTiposAsync()
    {
        if (_tiposCargados)
        {
            return;
        }

        try
        {
            var tipos = await catalogos.ListarAsync(TiposCatalogo.TiposEquipo, CancellationToken.None);
            _tiposCargados = true;
            var elegido = FiltroTipo ?? TodoTipo;
            _aplicandoVarios = true;
            OpcionesTipo = [TodoTipo, .. tipos.OrderBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(t => new OpcionFiltro(t.Id.ToString(), t.Nombre))];
            FiltroTipo = OpcionesTipo.FirstOrDefault(o => o == elegido) ?? TodoTipo;
            _aplicandoVarios = false;
        }
        catch (ErrorApi)
        {
            // Sin catálogo, el filtro queda en «Todo tipo de equipo»; la lista sigue funcionando.
        }
    }

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
            Texto: BuscarPor.Valor == "nombre" && texto.Length > 0 ? texto : null,
            TipoEquipoId: Guid.TryParse(FiltroTipo?.Valor, out var tipo) ? tipo : null,
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

    /// <summary>Pide las miniaturas de la página en paralelo; si el usuario ya cambió de página, se descartan.</summary>
    private async Task CargarMiniaturasAsync(IReadOnlyList<FilaEquipo> filas, int consulta)
    {
        if (fotos is null || !VerFotos)
        {
            return;
        }

        await Task.WhenAll(filas.Where(f => f.Equipo.TieneFoto).Select(async fila =>
        {
            var imagen = await fotos.DescargarAsync(fila.Equipo.Id, ReductorFotoWpf.LadoMiniatura, CancellationToken.None);
            if (consulta == _consulta)
            {
                fila.Foto.Imagen = imagen;
            }
        }));
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

            var hoy = Hoy();
            Filas = resultado.Items.Select(e => new FilaEquipo(e, hoy)).ToList();
            Total = resultado.Total;
            Pagina = pagina;
            Aviso = null;
            _ = CargarMiniaturasAsync(Filas, consulta);
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
    /// <summary>Miniatura de 128 px (RF-031); mientras no llega, o sin foto, el ícono del tipo.</summary>
    public MiniaturaEquipo Foto { get; } = new();

    public string Placa => Equipo.PlacaInventario;

    /// <summary>E-10: ícono del tipo de equipo (clave de Themes/Icons.xaml).</summary>
    public string IconoTipo => IconosTipoEquipo.Clave(Equipo.TipoEquipo);

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

/// <summary>Miniatura que llega después de la fila: la tabla aparece de inmediato y las fotos se completan.</summary>
public partial class MiniaturaEquipo : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayImagen))]
    private byte[]? _imagen;

    public bool HayImagen => Imagen is not null;
}
