using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-007 Registrar o editar equipo (HU-002, solo AC-02; prototipo v0.21, RegistrarEquipo.tsx).
/// Al elegir el tipo propone la clase de riesgo y la calibración (RF-030) y avisa si el equipo
/// difiere. El próximo preventivo se calcula como último + periodicidad (ajustable); si queda en el
/// pasado, el plan nace vencido; si no se conoce el último, se pide el próximo (CA-HU-002-06).
/// Valida en el cliente y muestra los errores de la API en cada campo (la API vuelve a validar todo).
/// </summary>
public partial class FormularioEquipoViewModel : ObservableObject
{
    private const string FormatoFecha = "dd/MM/yyyy";
    private const string Si = "si";
    private const string No = "no";

    private readonly DateOnly _hoy;
    private bool _calculando;
    private bool _proximoManual;

    public static IReadOnlyList<OpcionFiltro> OpcionesAutorizacion { get; } =
        [new("registro_sanitario", "Registro sanitario"), new("permiso_comercializacion", "Permiso de comercialización")];

    public static IReadOnlyList<OpcionFiltro> OpcionesClase { get; } =
        TextosEquipo.DescripcionClase.Select(c => new OpcionFiltro(c.Key, $"Clase {c.Key} · {c.Value.ToLower(TextosEquipo.Cultura)}")).ToList();

    public static IReadOnlyList<OpcionFiltro> OpcionesCalibracion { get; } = [new(Si, "Sí, requiere calibración"), new(No, "No requiere calibración")];

    /// <summary>Periodicidades del preventivo en días (prototipo: mensual a anual).</summary>
    public static IReadOnlyList<OpcionFiltro> OpcionesPeriodicidad { get; } =
        [new("30", "Mensual"), new("90", "Trimestral"), new("180", "Semestral"), new("365", "Anual")];

    /// <summary>Elemento «Sin proveedor» de la lista de proveedores (es opcional).</summary>
    public static readonly ElementoCatalogo SinProveedor = new(Guid.Empty, "Sin proveedor", true);

    public FormularioEquipoViewModel(
        IReadOnlyList<ElementoCatalogo> tipos,
        IReadOnlyList<ElementoCatalogo> ubicaciones,
        IReadOnlyList<ElementoCatalogo> proveedores,
        DateOnly hoy,
        Equipo? existente = null)
    {
        _hoy = hoy;
        Id = existente?.Id;

        // Solo los activos, más el que ya tiene el equipo aunque se haya desactivado.
        Tipos = Activos(tipos, existente?.TipoEquipoId);
        Ubicaciones = Activos(ubicaciones, existente?.UbicacionId);
        Proveedores = [SinProveedor, .. Activos(proveedores, existente?.ProveedorId)];

        _tipoAutorizacion = OpcionesAutorizacion[0];
        _periodicidad = OpcionesPeriodicidad[1];
        _proveedor = SinProveedor;
        if (existente is null)
        {
            return;
        }

        _placa = existente.PlacaInventario;
        _serie = existente.NumeroSerie;
        _tipo = Tipos.FirstOrDefault(t => t.Id == existente.TipoEquipoId);
        _marca = existente.Marca;
        _modelo = existente.Modelo;
        _tipoAutorizacion = OpcionesAutorizacion.FirstOrDefault(o => o.Valor == existente.TipoAutorizacion) ?? OpcionesAutorizacion[0];
        _registroInvima = existente.RegistroInvima ?? string.Empty;
        _venceRegistro = Texto(existente.VenceRegistro);
        _clase = OpcionesClase.FirstOrDefault(o => o.Valor == existente.ClaseRiesgo);
        _calibracion = existente.RequiereCalibracion ? OpcionesCalibracion[0] : OpcionesCalibracion[1];
        _mesesCalibracion = existente.PeriodicidadCalibracionMeses?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _ubicacion = Ubicaciones.FirstOrDefault(u => u.Id == existente.UbicacionId);
        _proveedor = Proveedores.FirstOrDefault(p => p.Id == existente.ProveedorId) ?? SinProveedor;
        _fechaAdquisicion = Texto(existente.FechaAdquisicion);
        if (existente.Preventivo is { } plan)
        {
            _periodicidad = OpcionesPeriodicidad.FirstOrDefault(o => o.Valor == plan.PeriodicidadDias.ToString(CultureInfo.InvariantCulture))
                ?? new OpcionFiltro(plan.PeriodicidadDias.ToString(CultureInfo.InvariantCulture), TextosEquipo.Periodicidad(plan.PeriodicidadDias));
            _ultimoPreventivo = Texto(plan.UltimaEjecucion);
            _noSeConoce = plan.UltimaEjecucion is null;
            _proximoPreventivo = Texto(plan.ProximaFecha);
            // Se conserva la fecha guardada; solo se marca «ajustada» si no coincide con último + periodicidad.
            _proximoManual = plan.UltimaEjecucion is { } ultima && ultima.AddDays(plan.PeriodicidadDias) != plan.ProximaFecha;
        }
    }

    public Guid? Id { get; }

    public bool EsNuevo => Id is null;

    public string Titulo => EsNuevo ? "Registrar equipo biomédico" : $"Editar equipo {Placa}";

    public string TextoGuardar => EsNuevo ? "Registrar equipo" : "Guardar cambios";

    public IReadOnlyList<ElementoCatalogo> Tipos { get; }

    public IReadOnlyList<ElementoCatalogo> Ubicaciones { get; }

    public IReadOnlyList<ElementoCatalogo> Proveedores { get; }

    /// <summary>Si es true, el catálogo de tipos o de ubicaciones está vacío y no se puede registrar.</summary>
    public bool FaltanCatalogos => Tipos.Count == 0 || Ubicaciones.Count == 0;

    // ---------- Identificación ----------

    [ObservableProperty]
    private string _placa = string.Empty;

    [ObservableProperty]
    private string _serie = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difiere), nameof(AyudaClase), nameof(AyudaCalibracion))]
    private ElementoCatalogo? _tipo;

    [ObservableProperty]
    private string _marca = string.Empty;

    [ObservableProperty]
    private string _modelo = string.Empty;

    // ---------- Información regulatoria (RF-030) ----------

    [ObservableProperty]
    private OpcionFiltro _tipoAutorizacion;

    [ObservableProperty]
    private string _registroInvima = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoVencimiento))]
    private string _venceRegistro = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difiere), nameof(AyudaClase))]
    private OpcionFiltro? _clase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difiere), nameof(RequiereCalibracion), nameof(AyudaCalibracion))]
    private OpcionFiltro? _calibracion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difiere))]
    private string _mesesCalibracion = string.Empty;

    public bool RequiereCalibracion => Calibracion?.Valor == Si;

    public string AyudaClase => Tipo?.ClaseRiesgo is { } sugerida
        ? $"El tipo sugiere clase {sugerida}. Usa la del registro sanitario del modelo."
        : "Según el registro sanitario del modelo (Decreto 4725 de 2005).";

    public string AyudaCalibracion => RequiereCalibracion || Calibracion is null
        ? "Si mide (presión, peso, temperatura, energía…), se calibra. Entre 1 y 60 meses."
        : "No se programarán calibraciones ni alertas de calibración (RN-010).";

    /// <summary>La clase o la calibración no coinciden con los valores del tipo: queda en la bitácora.</summary>
    public bool Difiere
    {
        get
        {
            if (Tipo is not { } tipo || Clase is null || Calibracion is null)
            {
                return false;
            }

            var meses = int.TryParse(MesesCalibracion, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : (int?)null;
            return (tipo.ClaseRiesgo is not null && tipo.ClaseRiesgo != Clase.Valor)
                || (tipo.RequiereCalibracion is { } calibra && calibra != RequiereCalibracion)
                || (RequiereCalibracion && tipo.RequiereCalibracion == true && tipo.PeriodicidadCalibracionMeses != meses);
        }
    }

    /// <summary>RN-010: aviso (no bloqueo) si la autorización ya venció o le faltan 180 días o menos.</summary>
    public string? AvisoVencimiento => Fecha(VenceRegistro) is not { } vence
        ? null
        : vence < _hoy
            ? "La autorización ya venció. Puedes registrar el equipo; quedará con el aviso de registro vencido."
            : vence.DayNumber - _hoy.DayNumber <= 180
                ? $"La autorización vence en {vence.DayNumber - _hoy.DayNumber} días. Verifica su renovación a tiempo."
                : null;

    // ---------- Ubicación y adquisición ----------

    [ObservableProperty]
    private ElementoCatalogo? _ubicacion;

    [ObservableProperty]
    private ElementoCatalogo _proveedor;

    [ObservableProperty]
    private string _fechaAdquisicion = string.Empty;

    // ---------- Mantenimiento preventivo (CA-HU-002-06) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyudaProximo))]
    private OpcionFiltro _periodicidad;

    [ObservableProperty]
    private string _ultimoPreventivo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PideUltimo), nameof(AyudaProximo), nameof(PreventivoAtrasado))]
    private bool _noSeConoce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyudaProximo), nameof(PreventivoAtrasado))]
    private string _proximoPreventivo = string.Empty;

    public bool PideUltimo => !NoSeConoce;

    public string AyudaProximo => NoSeConoce
        ? "Fecha en la que se hará el primer preventivo en SITBIO."
        : _proximoManual
            ? "Ajustado a mano."
            : $"Se calcula como último + {Periodicidad.Texto.ToLower(TextosEquipo.Cultura)}. Puedes ajustarlo.";

    /// <summary>RF-020: el próximo calculado ya pasó; el plan nace vencido y aparece en las alertas.</summary>
    public bool PreventivoAtrasado => !NoSeConoce && Fecha(ProximoPreventivo) is { } proximo && proximo < _hoy;

    // ---------- Errores ----------

    [ObservableProperty]
    private string? _errorPlaca;

    [ObservableProperty]
    private string? _errorSerie;

    [ObservableProperty]
    private string? _errorTipo;

    [ObservableProperty]
    private string? _errorMarca;

    [ObservableProperty]
    private string? _errorModelo;

    [ObservableProperty]
    private string? _errorRegistro;

    [ObservableProperty]
    private string? _errorVence;

    [ObservableProperty]
    private string? _errorClase;

    [ObservableProperty]
    private string? _errorMeses;

    [ObservableProperty]
    private string? _errorUbicacion;

    [ObservableProperty]
    private string? _errorAdquisicion;

    [ObservableProperty]
    private string? _errorUltimo;

    [ObservableProperty]
    private string? _errorProximo;

    /// <summary>MSG-008 (revisar campos), MSG-019 (límite del plan) u otro error de la API.</summary>
    [ObservableProperty]
    private Aviso? _aviso;

    // ---------- Reglas del formulario ----------

    /// <summary>Al elegir el tipo se proponen su clase y su calibración (CA-HU-002-04).</summary>
    partial void OnTipoChanged(ElementoCatalogo? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.ClaseRiesgo is { } clase)
        {
            Clase = OpcionesClase.FirstOrDefault(o => o.Valor == clase);
        }

        if (value.RequiereCalibracion is { } calibra)
        {
            Calibracion = calibra ? OpcionesCalibracion[0] : OpcionesCalibracion[1];
            MesesCalibracion = calibra ? value.PeriodicidadCalibracionMeses?.ToString(CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        }
    }

    partial void OnCalibracionChanged(OpcionFiltro? value)
    {
        if (value?.Valor == No)
        {
            MesesCalibracion = string.Empty;
            ErrorMeses = null;
        }
    }

    partial void OnUltimoPreventivoChanged(string value) => CalcularProximo();

    partial void OnPeriodicidadChanged(OpcionFiltro value) => CalcularProximo();

    partial void OnNoSeConoceChanged(bool value)
    {
        if (value)
        {
            UltimoPreventivo = string.Empty;
            ErrorUltimo = null;
            _calculando = true;
            ProximoPreventivo = string.Empty;
            _calculando = false;
            _proximoManual = false;
        }
        else
        {
            CalcularProximo();
        }
    }

    partial void OnProximoPreventivoChanged(string value)
    {
        if (!_calculando)
        {
            _proximoManual = !NoSeConoce && value.Trim().Length > 0;
            OnPropertyChanged(nameof(AyudaProximo));
        }
    }

    /// <summary>Próximo = último + periodicidad, salvo que el usuario lo haya ajustado.</summary>
    private void CalcularProximo()
    {
        if (NoSeConoce || (_proximoManual && ProximoPreventivo.Trim().Length > 0))
        {
            return;
        }

        _proximoManual = false;
        _calculando = true;
        ProximoPreventivo = Fecha(UltimoPreventivo) is { } ultimo && int.TryParse(Periodicidad.Valor, out var dias)
            ? Texto(ultimo.AddDays(dias))
            : string.Empty;
        _calculando = false;
        OnPropertyChanged(nameof(AyudaProximo));
    }

    /// <summary>Validación previa; la API vuelve a validar todo.</summary>
    public bool Validar()
    {
        const string Obligatorio = "Este campo es obligatorio.";
        const string FechaInvalida = "Escribe la fecha como dd/mm/aaaa.";

        ErrorPlaca = Vacio(Placa) ? Obligatorio : null;
        ErrorSerie = Vacio(Serie) ? Obligatorio : null;
        ErrorTipo = Tipo is null ? "Elige el tipo de equipo." : null;
        ErrorMarca = Vacio(Marca) ? Obligatorio : null;
        ErrorModelo = Vacio(Modelo) ? Obligatorio : null;
        ErrorRegistro = Vacio(RegistroInvima) ? "Escribe el número tal como aparece en el registro o permiso." : null;
        ErrorVence = Vacio(VenceRegistro) ? Obligatorio : Fecha(VenceRegistro) is null ? FechaInvalida : null;
        ErrorClase = Clase is null ? "Elige la clase de riesgo." : null;
        ErrorMeses = RequiereCalibracion && (!int.TryParse(MesesCalibracion, NumberStyles.None, CultureInfo.InvariantCulture, out var meses) || meses is < 1 or > 60)
            ? "Escribe un número de meses entre 1 y 60."
            : null;
        ErrorUbicacion = Ubicacion is null ? "Elige el área." : null;
        ErrorAdquisicion = !Vacio(FechaAdquisicion) && Fecha(FechaAdquisicion) is null
            ? FechaInvalida
            : Fecha(FechaAdquisicion) > _hoy ? "La fecha de adquisición no puede ser futura." : null;

        if (NoSeConoce)
        {
            ErrorUltimo = null;
            ErrorProximo = Fecha(ProximoPreventivo) is not { } proximo
                ? (Vacio(ProximoPreventivo) ? "Indica la fecha del próximo preventivo." : FechaInvalida)
                : proximo < _hoy ? "El próximo preventivo no puede ser una fecha pasada." : null;
        }
        else
        {
            var ultimo = Fecha(UltimoPreventivo);
            ErrorUltimo = ultimo is null
                ? (Vacio(UltimoPreventivo) ? "Indica el último preventivo o marca «No se conoce»." : FechaInvalida)
                : ultimo > _hoy ? "El último preventivo no puede ser una fecha futura." : null;
            var proximo = Fecha(ProximoPreventivo);
            ErrorProximo = !Vacio(ProximoPreventivo) && proximo is null
                ? FechaInvalida
                : ultimo is not null && proximo is not null && proximo <= ultimo ? "El próximo preventivo debe ser posterior al último." : null;
        }

        var valido = new[]
        {
            ErrorPlaca, ErrorSerie, ErrorTipo, ErrorMarca, ErrorModelo, ErrorRegistro, ErrorVence, ErrorClase, ErrorMeses,
            ErrorUbicacion, ErrorAdquisicion, ErrorUltimo, ErrorProximo,
        }.All(e => e is null);
        Aviso = valido ? null : CamposMarcados();
        return valido;
    }

    public DatosEquipo Datos() => new(
        Tipo?.Id,
        Ubicacion?.Id,
        Proveedor.Id == Guid.Empty ? null : Proveedor.Id,
        Serie.Trim(),
        Placa.Trim(),
        Marca.Trim(),
        Modelo.Trim(),
        Fecha(FechaAdquisicion),
        TipoAutorizacion.Valor,
        RegistroInvima.Trim(),
        Fecha(VenceRegistro),
        Clase?.Valor,
        Calibracion is null ? null : RequiereCalibracion,
        RequiereCalibracion && int.TryParse(MesesCalibracion, NumberStyles.None, CultureInfo.InvariantCulture, out var meses) ? meses : null,
        new DatosPreventivo(
            int.Parse(Periodicidad.Valor, CultureInfo.InvariantCulture),
            NoSeConoce ? null : Fecha(UltimoPreventivo),
            Fecha(ProximoPreventivo)));

    /// <summary>Ubica el error de la API: RN-001 en la placa o la serie, RN-009 como MSG-019, 400 en cada campo.</summary>
    public void MostrarError(ErrorApi error)
    {
        switch (error.Codigo)
        {
            case "RN-001":
                if (error.Message.Contains("placa", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorPlaca = error.Message;
                }
                else
                {
                    ErrorSerie = error.Message;
                }

                Aviso = CamposMarcados();
                return;
            case "RN-009":
                Aviso = new Aviso("Límite del plan alcanzado", error.Message, "MSG-019");
                return;
        }

        if (error.Errores.Count > 0)
        {
            string? De(params string[] campos) => campos.Select(c => error.Errores.GetValueOrDefault(c)).FirstOrDefault(m => m is not null);
            ErrorPlaca = De("placaInventario");
            ErrorSerie = De("numeroSerie");
            ErrorTipo = De("tipoEquipoId");
            ErrorMarca = De("marca");
            ErrorModelo = De("modelo");
            ErrorRegistro = De("registroInvima", "tipoAutorizacion");
            ErrorVence = De("venceRegistro");
            ErrorClase = De("claseRiesgo");
            ErrorMeses = De("periodicidadCalibracionMeses", "requiereCalibracion");
            ErrorUbicacion = De("ubicacionId");
            ErrorAdquisicion = De("fechaAdquisicion", "proveedorId");
            ErrorUltimo = De("preventivo.ultimaEjecucion", "preventivo.periodicidadDias");
            ErrorProximo = De("preventivo.proximaFecha", "preventivo.descripcion");
            Aviso = CamposMarcados();
            return;
        }

        Aviso = new Aviso(
            error.SinConexion ? "Sin conexión" : EsNuevo ? "No se registró el equipo" : "No se guardaron los cambios",
            error.Message,
            error.Codigo ?? (error.SinConexion ? "MSG-011" : null));
    }

    // ---------- Apoyo ----------

    public static DateOnly? Fecha(string texto) =>
        DateOnly.TryParseExact(texto.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], TextosEquipo.Cultura, DateTimeStyles.None, out var fecha) ? fecha : null;

    private static string Texto(DateOnly? fecha) => fecha?.ToString(FormatoFecha, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool Vacio(string texto) => string.IsNullOrWhiteSpace(texto);

    private static List<ElementoCatalogo> Activos(IReadOnlyList<ElementoCatalogo> elementos, Guid? actual) =>
        elementos.Where(e => e.Activo || e.Id == actual).ToList();

    private Aviso CamposMarcados() => new(
        "Revisa los campos marcados",
        $"Corrige la información para poder {(EsNuevo ? "registrar el equipo" : "guardar los cambios")}.",
        "MSG-008");
}
