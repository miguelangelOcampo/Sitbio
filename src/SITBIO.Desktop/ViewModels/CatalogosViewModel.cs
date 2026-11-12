using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-020 Catálogos (HU-025, RF-025; prototipo Admin.tsx › Catalogos). Lista de catálogos a la
/// izquierda y sus valores a la derecha. El Ingeniero Biomédico (AC-02) gestiona los cuatro; el
/// Administrador (AC-01) solo las ubicaciones y consulta los demás (CA-HU-025-03). Los valores no
/// se borran: se inactivan (CA-HU-025-02). En «Tipos de equipo» se ven y editan la clase de riesgo y
/// la calibración que se proponen al registrar un equipo (RF-030).
/// </summary>
public partial class CatalogosViewModel(IServicioCatalogos servicio, SesionActual sesion) : ObservableObject
{
    /// <summary>Clases de riesgo del Decreto 4725 de 2005; vacío = «Por definir».</summary>
    public static IReadOnlyList<OpcionFiltro> OpcionesClase { get; } =
    [
        new(string.Empty, "Por definir"),
        new("I", "Clase I"),
        new("IIa", "Clase IIa"),
        new("IIb", "Clase IIb"),
        new("III", "Clase III"),
    ];

    public ObservableCollection<CatalogoVista> Catalogos { get; } =
    [
        new(TiposCatalogo.TiposEquipo, "Tipos de equipo"),
        new(TiposCatalogo.Ubicaciones, "Ubicaciones"),
        new(TiposCatalogo.Proveedores, "Proveedores"),
        new(TiposCatalogo.TiposFalla, "Tipos de falla"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeGestionar), nameof(EsTiposEquipo), nameof(EsProveedores), nameof(Resumen), nameof(Filas), nameof(PlaceholderNuevo), nameof(AyudaValores))]
    [NotifyCanExecuteChangedFor(nameof(AgregarCommand))]
    private CatalogoVista? _seleccionado;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarCommand), nameof(CambiarEstadoCommand), nameof(GuardarValoresCommand))]
    private bool _guardando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    private string? Rol => sesion.Sesion?.Rol;

    /// <summary>AC-02 gestiona todos; AC-01 solo ubicaciones (como el prototipo v0.20).</summary>
    public bool PuedeGestionar => Seleccionado is { } c && (Rol == "AC-02" || (Rol == "AC-01" && c.Tipo == TiposCatalogo.Ubicaciones));

    public bool EsTiposEquipo => Seleccionado?.Tipo == TiposCatalogo.TiposEquipo;

    public bool EsProveedores => Seleccionado?.Tipo == TiposCatalogo.Proveedores;

    public string Resumen => Seleccionado is not { } c ? string.Empty
        : $"{c.Activos} activos de {c.Elementos.Count}" + (PuedeGestionar ? string.Empty : " · solo consulta (lo gestiona el Ingeniero Biomédico)");

    public IReadOnlyList<FilaCatalogo> Filas => Seleccionado?.Elementos.Select(e => new FilaCatalogo(e)).ToList() ?? [];

    public string PlaceholderNuevo => Seleccionado is { } c ? $"Nuevo valor para {c.Nombre.ToLower(TextosEquipo.Cultura)}" : string.Empty;

    // ---------- Nuevo valor ----------

    [ObservableProperty]
    private string _nuevoValor = string.Empty;

    [ObservableProperty]
    private string _nuevoNit = string.Empty;

    [ObservableProperty]
    private string _nuevoTipoProveedor = string.Empty;

    [ObservableProperty]
    private string? _errorNuevo;

    partial void OnNuevoValorChanged(string value) => ErrorNuevo = null;

    partial void OnNuevoNitChanged(string value) => ErrorNuevo = null;

    partial void OnNuevoTipoProveedorChanged(string value) => ErrorNuevo = null;

    // ---------- Valores regulatorios del tipo de equipo elegido (RF-030, solo AC-02) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditandoValores), nameof(AyudaValores))]
    [NotifyCanExecuteChangedFor(nameof(GuardarValoresCommand))]
    private FilaCatalogo? _filaSeleccionada;

    public bool EditandoValores => EsTiposEquipo && PuedeGestionar && FilaSeleccionada is not null;

    /// <summary>Indicación para AC-02 en «Tipos de equipo» mientras no haya un tipo elegido.</summary>
    public bool AyudaValores => EsTiposEquipo && PuedeGestionar && FilaSeleccionada is null;

    [ObservableProperty]
    private OpcionFiltro _clase = OpcionesClase[0];

    [ObservableProperty]
    private bool _requiereCalibracion = true;

    [ObservableProperty]
    private string _meses = "12";

    [ObservableProperty]
    private string? _errorMeses;

    partial void OnMesesChanged(string value) => ErrorMeses = null;

    partial void OnFilaSeleccionadaChanged(FilaCatalogo? value)
    {
        if (value?.Elemento is not { } e)
        {
            return;
        }

        Clase = OpcionesClase.FirstOrDefault(o => o.Valor == (e.ClaseRiesgo ?? string.Empty)) ?? OpcionesClase[0];
        RequiereCalibracion = e.RequiereCalibracion ?? true;
        Meses = (e.PeriodicidadCalibracionMeses ?? 12).ToString(CultureInfo.InvariantCulture);
    }

    partial void OnSeleccionadoChanged(CatalogoVista? value)
    {
        foreach (var catalogo in Catalogos)
        {
            catalogo.Elegido = catalogo == value;
        }

        FilaSeleccionada = null;
        (NuevoValor, NuevoNit, NuevoTipoProveedor, ErrorNuevo) = (string.Empty, string.Empty, string.Empty, null);
        OnPropertyChanged(nameof(EditandoValores));
    }

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            foreach (var catalogo in Catalogos)
            {
                catalogo.Elementos = await servicio.ListarAsync(catalogo.Tipo, CancellationToken.None);
            }

            Aviso = null;
            Seleccionado ??= Catalogos[0];
            Refrescar();
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir los catálogos",
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
    private void Elegir(CatalogoVista catalogo) => Seleccionado = catalogo;

    [RelayCommand(CanExecute = nameof(PuedeAgregar))]
    private async Task AgregarAsync()
    {
        if (Seleccionado is not { } catalogo)
        {
            return;
        }

        Confirmacion = null;
        var valor = NuevoValor.Trim();
        ErrorNuevo = valor.Length == 0 ? "Escribe el valor."
            : catalogo.Elementos.Any(e => string.Equals(e.Nombre.Trim(), valor, StringComparison.CurrentCultureIgnoreCase)) ? "Ese valor ya existe en la lista."
            : EsProveedores && (NuevoNit.Trim().Length == 0 || NuevoTipoProveedor.Trim().Length == 0) ? "Escribe el NIT y el tipo de proveedor."
            : null;
        if (ErrorNuevo is not null)
        {
            return;
        }

        var datos = EsProveedores ? new DatosCatalogo(valor, Nit: NuevoNit.Trim(), TipoProveedor: NuevoTipoProveedor.Trim()) : new DatosCatalogo(valor);
        Guardando = true;
        try
        {
            await servicio.CrearAsync(catalogo.Tipo, datos, CancellationToken.None);
            await RecargarAsync(catalogo);
            (NuevoValor, NuevoNit, NuevoTipoProveedor) = (string.Empty, string.Empty, string.Empty);
            Aviso = null;
            Confirmacion = new Aviso("Valor agregado", $"“{valor}” ya está disponible en {catalogo.Nombre.ToLower(TextosEquipo.Cultura)}.", null, EsError: false);
        }
        catch (ErrorApi error)
        {
            Fallo(error, "No se pudo agregar el valor");
            ErrorNuevo = error.SinConexion ? null : error.Message;
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeAgregar() => !Guardando && PuedeGestionar;

    /// <summary>Activa o inactiva un valor (CA-HU-025-02). Se envía el detalle completo para no perder datos.</summary>
    [RelayCommand(CanExecute = nameof(Libre))]
    private async Task CambiarEstadoAsync(FilaCatalogo fila)
    {
        if (Seleccionado is not { } catalogo || !PuedeGestionar)
        {
            return;
        }

        Confirmacion = null;
        Guardando = true;
        try
        {
            var detalle = await servicio.ObtenerAsync(catalogo.Tipo, fila.Elemento.Id, CancellationToken.None);
            var activo = !detalle.Activo;
            await servicio.ActualizarAsync(catalogo.Tipo, detalle.Id, detalle.ComoDatos(activo), CancellationToken.None);
            await RecargarAsync(catalogo);
            Aviso = null;
            Confirmacion = activo
                ? new Aviso("Valor activado", $"“{detalle.Nombre}” vuelve a estar disponible.", null, EsError: false)
                : new Aviso("Valor inactivado", $"“{detalle.Nombre}” ya no se ofrece en los formularios; los registros que lo usan lo conservan.", null, EsError: false);
        }
        catch (ErrorApi error)
        {
            Fallo(error, "No se pudo cambiar el estado");
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool Libre() => !Guardando;

    /// <summary>Clase de riesgo y calibración por defecto del tipo de equipo elegido (RF-030).</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarValores))]
    private async Task GuardarValoresAsync()
    {
        if (Seleccionado is not { } catalogo || FilaSeleccionada is not { } fila || !EditandoValores)
        {
            return;
        }

        Confirmacion = null;
        int? meses = null;
        if (RequiereCalibracion)
        {
            if (!int.TryParse(Meses.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor is < 1 or > 60)
            {
                ErrorMeses = "Usa un valor entre 1 y 60 meses.";
                return;
            }

            meses = valor;
        }

        Guardando = true;
        try
        {
            var detalle = await servicio.ObtenerAsync(catalogo.Tipo, fila.Elemento.Id, CancellationToken.None);
            var datos = detalle.ComoDatos() with
            {
                ClaseRiesgo = Clase.Valor.Length == 0 ? null : Clase.Valor,
                RequiereCalibracion = RequiereCalibracion,
                PeriodicidadCalibracionMeses = meses,
            };
            await servicio.ActualizarAsync(catalogo.Tipo, detalle.Id, datos, CancellationToken.None);
            await RecargarAsync(catalogo);
            Aviso = null;
            Confirmacion = new Aviso(
                "Valores por defecto guardados",
                $"Al registrar un equipo de tipo “{detalle.Nombre}” se propondrán estos valores (RF-030).",
                null,
                EsError: false);
        }
        catch (ErrorApi error)
        {
            ErrorMeses = error.Errores.GetValueOrDefault("periodicidadCalibracionMeses");
            Fallo(error, "No se pudieron guardar los valores");
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeGuardarValores() => !Guardando && FilaSeleccionada is not null;

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    private async Task RecargarAsync(CatalogoVista catalogo)
    {
        var id = FilaSeleccionada?.Elemento.Id;
        catalogo.Elementos = await servicio.ListarAsync(catalogo.Tipo, CancellationToken.None);
        Refrescar();
        FilaSeleccionada = id is null ? null : Filas.FirstOrDefault(f => f.Elemento.Id == id);
    }

    private void Refrescar()
    {
        OnPropertyChanged(nameof(Resumen));
        OnPropertyChanged(nameof(Filas));
    }

    private void Fallo(ErrorApi error, string titulo) =>
        Aviso = new Aviso(error.SinConexion ? "Sin conexión" : titulo, error.Message, error.SinConexion ? "MSG-011" : error.Codigo);
}

/// <summary>Catálogo de la lista de la izquierda con el conteo de activos.</summary>
public sealed partial class CatalogoVista(string tipo, string nombre) : ObservableObject
{
    public string Tipo { get; } = tipo;

    public string Nombre { get; } = nombre;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Activos), nameof(TextoActivos))]
    private IReadOnlyList<ElementoCatalogo> _elementos = [];

    /// <summary>Marca la fila de la lista con la barra de selección.</summary>
    [ObservableProperty]
    private bool _elegido;

    public int Activos => Elementos.Count(e => e.Activo);

    public string TextoActivos => $"{Activos} activos";
}

/// <summary>Fila de la tabla de valores: estado, acción y, en tipos de equipo, sus valores regulatorios.</summary>
public sealed record FilaCatalogo(ElementoCatalogo Elemento)
{
    public string Valor => Elemento.Nombre;

    public string Estado => Elemento.Activo ? "Activo" : "Inactivo";

    public string Accion => Elemento.Activo ? "Inactivar" : "Activar";

    public string ClaseRiesgo => Elemento.ClaseRiesgo is { Length: > 0 } c ? $"Clase {c}" : "Por definir";

    public string Calibracion => Elemento.RequiereCalibracion switch
    {
        true when Elemento.PeriodicidadCalibracionMeses is { } meses => $"Sí · cada {meses} meses",
        true => "Sí",
        false => "No requiere",
        null => "Por definir",
    };

    /// <summary>NIT y tipo del proveedor, para la columna «Detalle».</summary>
    public string Detalle => Elemento.Nit is { Length: > 0 } nit ? $"NIT {nit} · {Elemento.TipoProveedor}" : TextosEquipo.SinDato;
}
