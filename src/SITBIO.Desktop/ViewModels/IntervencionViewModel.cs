using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-013 Intervención (HU-005, HU-006; RF-005, RF-006). La hoja de servicio de una orden: diagnóstico,
/// acciones, repuestos, costos, tiempo, estado de salida del equipo y los campos dinámicos de la versión
/// activa del tipo de equipo, cada uno con el control de su <c>tipoDato</c>. Valida en el cliente lo
/// evidente y muestra junto a cada campo lo que rechace la API. Solo el técnico la edita, con la orden en
/// ejecución o pausada; los demás la ven en solo lectura.
/// </summary>
public partial class IntervencionViewModel(
    IServicioOrdenes ordenes,
    IServicioEquipos equipos,
    IServicioConfiguracionCampos configuracion,
    Orden orden,
    bool editable,
    Action<Orden?> volver) : ObservableObject
{
    public const string IdPantalla = "UI-013";
    public const string Obligatorio = "Este campo es obligatorio.";
    public const string NumeroInvalido = "Escribe un número válido.";
    public const string SinCamposDinamicos = "Este tipo de equipo no tiene campos adicionales.";

    public Orden Orden { get; private set; } = orden;

    /// <summary>Se puede guardar (técnico, orden en ejecución o pausada, intervención sin firmar).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _editable = editable;

    public string Referencia => Orden.CodigoTiquete ?? Orden.PlacaInventario;

    public string Equipo => string.IsNullOrEmpty(Orden.TipoEquipo) ? Orden.PlacaInventario : $"{Orden.PlacaInventario} · {Orden.TipoEquipo}";

    public string EstadoOrden => EstadosOrden.Nombre(Orden.Estado);

    public static IReadOnlyList<string> OpcionesSalida => EstadosSalidaEquipo.Todos;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _guardando;

    [ObservableProperty]
    private Aviso? _aviso;

    /// <summary>Nota de solo lectura o de cambio de versión de los campos.</summary>
    [ObservableProperty]
    private string? _nota;

    [ObservableProperty]
    private string _diagnostico = string.Empty;

    [ObservableProperty]
    private string? _errorDiagnostico;

    [ObservableProperty]
    private string _accionesRealizadas = string.Empty;

    [ObservableProperty]
    private string? _errorAcciones;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostoTotal))]
    private string _costoManoObra = string.Empty;

    [ObservableProperty]
    private string? _errorManoObra;

    [ObservableProperty]
    private string _tiempoAtencion = string.Empty;

    [ObservableProperty]
    private string? _errorTiempo;

    [ObservableProperty]
    private string? _estadoSalida;

    [ObservableProperty]
    private string? _errorSalida;

    [ObservableProperty]
    private string? _errorRepuestos;

    public ObservableCollection<FilaRepuesto> Repuestos { get; } = [];

    public bool SinRepuestos => Repuestos.Count == 0;

    public bool HayRepuestos => Repuestos.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCampos))]
    private IReadOnlyList<CampoIntervencion> _campos = [];

    /// <summary>Versión de <c>configuracion_campos</c> con la que se arma el formulario.</summary>
    [ObservableProperty]
    private int? _versionCampos;

    public bool Cargado { get; private set; }

    public bool SinCampos => Cargado && Campos.Count == 0;

    /// <summary>Repuestos (cantidad × costo) más mano de obra, como lo calcula la API.</summary>
    public string CostoTotal => (Repuestos.Sum(r => r.Subtotal) + (Decimal(CostoManoObra) ?? 0m)).ToString("C0", TextosEquipo.Cultura);

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        Aviso = null;
        try
        {
            var equipo = await equipos.ObtenerAsync(Orden.EquipoId, CancellationToken.None);
            var config = configuracion.ObtenerAsync(equipo.TipoEquipoId, CancellationToken.None);
            var existente = ordenes.ObtenerIntervencionAsync(Orden.Id, CancellationToken.None);
            var (activa, guardada) = (await config, await existente);

            VersionCampos = activa.Version;
            Campos = activa.Campos.Select(c => new CampoIntervencion(c)).ToList();
            if (guardada is not null)
            {
                Mostrar(guardada);
            }

            if (guardada?.CerradaFirmada == true)
            {
                Editable = false;
                Nota = "La intervención está firmada y aprobada; ya no se puede modificar (RN-004).";
            }
            else if (!Editable)
            {
                Nota = Orden.Estado == EstadosOrden.Asignada
                    ? "Inicia la orden para registrar la intervención."
                    : "Estás viendo la intervención en solo lectura.";
            }
            else if (guardada?.VersionCampos is { } anterior && anterior != activa.Version)
            {
                Nota = $"Se registró con la versión {anterior} de los campos; al guardar se valida con la versión vigente ({activa.Version?.ToString(CultureInfo.InvariantCulture) ?? "sin campos"}).";
            }

            foreach (var campo in Campos)
            {
                campo.Editable = Editable;
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo abrir la intervención",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargado = true;
            Cargando = false;
            OnPropertyChanged(nameof(SinCampos));
        }
    }

    [RelayCommand]
    private void AgregarRepuesto()
    {
        var fila = new FilaRepuesto();
        fila.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilaRepuesto.Subtotal))
            {
                OnPropertyChanged(nameof(CostoTotal));
            }
        };
        Repuestos.Add(fila);
        ErrorRepuestos = null;
        OnPropertyChanged(nameof(CostoTotal));
        OnPropertyChanged(nameof(SinRepuestos));
        OnPropertyChanged(nameof(HayRepuestos));
    }

    [RelayCommand]
    private void QuitarRepuesto(FilaRepuesto? fila)
    {
        if (fila is not null && Repuestos.Remove(fila))
        {
            OnPropertyChanged(nameof(CostoTotal));
            OnPropertyChanged(nameof(SinRepuestos));
            OnPropertyChanged(nameof(HayRepuestos));
        }
    }

    [RelayCommand]
    private void Cancelar() => volver(null);

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (!Validar(out var datos))
        {
            return;
        }

        Guardando = true;
        Aviso = null;
        try
        {
            await ordenes.RegistrarIntervencionAsync(Orden.Id, datos, CancellationToken.None);
            volver(Orden with { TieneIntervencion = true });
        }
        catch (ErrorApi error)
        {
            MostrarErrores(error);
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeGuardar() => Editable && !Cargando && !Guardando;

    /// <summary>Validación del cliente: obligatorios y números. Lo demás lo valida la API.</summary>
    public bool Validar(out DatosIntervencion datos)
    {
        ErrorDiagnostico = string.IsNullOrWhiteSpace(Diagnostico) ? Obligatorio : null;
        ErrorAcciones = string.IsNullOrWhiteSpace(AccionesRealizadas) ? Obligatorio : null;
        var manoObra = Decimal(CostoManoObra);
        ErrorManoObra = CostoManoObra.Trim().Length > 0 && manoObra is not >= 0 ? "Escribe un valor en pesos mayor o igual a cero." : null;
        int? tiempo = null;
        if (TiempoAtencion.Trim().Length > 0)
        {
            tiempo = int.TryParse(TiempoAtencion.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutos) && minutos > 0 ? minutos : null;
            ErrorTiempo = tiempo is null ? "Escribe los minutos como un número entero mayor que cero." : null;
        }
        else
        {
            ErrorTiempo = null;
        }

        ErrorSalida = null;
        ErrorRepuestos = null;
        var repuestos = new List<Repuesto>();
        var repuestosValidos = true;
        foreach (var fila in Repuestos)
        {
            if (fila.Leer() is { } repuesto)
            {
                repuestos.Add(repuesto);
            }
            else
            {
                repuestosValidos = false;
            }
        }

        var valores = new Dictionary<string, object>(StringComparer.Ordinal);
        var camposValidos = true;
        foreach (var campo in Campos)
        {
            if (!campo.Leer(out var valor))
            {
                camposValidos = false;
            }
            else if (valor is not null)
            {
                valores[campo.Campo.Nombre] = valor;
            }
        }

        datos = new DatosIntervencion(Diagnostico.Trim(), AccionesRealizadas.Trim(), repuestos, manoObra, tiempo, EstadoSalida, valores);
        var valido = (ErrorDiagnostico ?? ErrorAcciones ?? ErrorManoObra ?? ErrorTiempo) is null && repuestosValidos && camposValidos;
        if (!valido)
        {
            Aviso = new Aviso("Revisa la intervención", "Hay campos por completar o corregir; están marcados en el formulario.", null);
        }

        return valido;
    }

    private void Mostrar(Intervencion i)
    {
        Diagnostico = i.Diagnostico;
        AccionesRealizadas = i.AccionesRealizadas;
        CostoManoObra = i.CostoManoObra?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        TiempoAtencion = i.TiempoAtencionMin?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        EstadoSalida = i.EstadoEquipoSalida;
        Repuestos.Clear();
        foreach (var r in i.Repuestos)
        {
            AgregarRepuesto();
            var fila = Repuestos[^1];
            fila.Nombre = r.Nombre;
            fila.Cantidad = r.Cantidad.ToString(CultureInfo.InvariantCulture);
            fila.CostoUnitario = r.CostoUnitario?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        foreach (var campo in Campos)
        {
            if (i.CamposDinamicos.TryGetValue(campo.Campo.Nombre, out var valor))
            {
                campo.Mostrar(valor);
            }
        }
    }

    /// <summary>Errores de la API junto a su campo; lo que no es de un campo (409, 503, permisos) va al aviso.</summary>
    private void MostrarErrores(ErrorApi error)
    {
        var sinUbicar = new List<string>();
        foreach (var (clave, mensaje) in error.Errores)
        {
            if (!Ubicar(clave, mensaje))
            {
                sinUbicar.Add(mensaje);
            }
        }

        if (error.Errores.Count == 0 || sinUbicar.Count > 0)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo guardar la intervención",
                sinUbicar.Count > 0 ? string.Join(" ", sinUbicar) : error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        else
        {
            Aviso = new Aviso("Revisa la intervención", "El servidor rechazó algunos datos; están marcados en el formulario.", null);
        }

        if (error.Codigo == "RN-004")
        {
            Editable = false;
        }
    }

    private bool Ubicar(string clave, string mensaje)
    {
        const string prefijo = "camposDinamicos.";
        if (clave.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            var campo = Campos.FirstOrDefault(c => string.Equals(c.Campo.Nombre, clave[prefijo.Length..], StringComparison.Ordinal));
            if (campo is null)
            {
                return false;
            }

            campo.Error = mensaje;
            return true;
        }

        if (clave.StartsWith("repuestos", StringComparison.OrdinalIgnoreCase))
        {
            ErrorRepuestos = mensaje;
            return true;
        }

        switch (clave.ToLowerInvariant())
        {
            case "diagnostico":
                ErrorDiagnostico = mensaje;
                return true;
            case "accionesrealizadas":
                ErrorAcciones = mensaje;
                return true;
            case "costomanoobra":
                ErrorManoObra = mensaje;
                return true;
            case "tiempoatencionmin":
                ErrorTiempo = mensaje;
                return true;
            case "estadoequiposalida":
                ErrorSalida = mensaje;
                return true;
            default:
                return false;
        }
    }

    partial void OnDiagnosticoChanged(string value) => ErrorDiagnostico = null;

    partial void OnAccionesRealizadasChanged(string value) => ErrorAcciones = null;

    partial void OnCostoManoObraChanged(string value) => ErrorManoObra = null;

    partial void OnTiempoAtencionChanged(string value) => ErrorTiempo = null;

    /// <summary>Acepta coma o punto decimal y separadores de miles con punto («15.000»).</summary>
    public static decimal? Decimal(string texto)
    {
        var limpio = texto.Trim().Replace("$", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (limpio.Length == 0)
        {
            return null;
        }

        if (limpio.Contains(',', StringComparison.Ordinal))
        {
            limpio = limpio.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (limpio.Count(c => c == '.') > 1 || (limpio.IndexOf('.', StringComparison.Ordinal) is var punto and >= 0 && limpio.Length - punto - 1 == 3))
        {
            limpio = limpio.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) ? valor : null;
    }
}

/// <summary>Repuesto en edición: nombre, cantidad y costo unitario (opcional).</summary>
public partial class FilaRepuesto : ObservableObject
{
    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtotal))]
    private string _cantidad = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtotal))]
    private string _costoUnitario = string.Empty;

    [ObservableProperty]
    private string? _error;

    public decimal Subtotal =>
        int.TryParse(Cantidad.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n * (IntervencionViewModel.Decimal(CostoUnitario) ?? 0m)
            : 0m;

    partial void OnNombreChanged(string value) => Error = null;

    partial void OnCantidadChanged(string value) => Error = null;

    partial void OnCostoUnitarioChanged(string value) => Error = null;

    public Repuesto? Leer()
    {
        var nombre = Nombre.Trim();
        var cantidadValida = int.TryParse(Cantidad.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cantidad) && cantidad is >= 1 and <= 10_000;
        var costo = IntervencionViewModel.Decimal(CostoUnitario);
        var costoValido = CostoUnitario.Trim().Length == 0 || costo is >= 0;
        Error = nombre.Length == 0 ? "Escribe el nombre del repuesto."
            : !cantidadValida ? "La cantidad va de 1 a 10.000."
            : !costoValido ? "El costo unitario debe ser un valor en pesos mayor o igual a cero."
            : null;
        return Error is null ? new Repuesto(nombre, cantidad, costo) : null;
    }
}

/// <summary>
/// Campo dinámico del formulario con el control de su tipo: casilla de texto (número, decimal, texto o
/// fecha dd/mm/aaaa), sí/no o lista de opciones. Convierte el texto al tipo JSON que espera la API.
/// </summary>
public partial class CampoIntervencion(CampoDinamico campo) : ObservableObject
{
    public CampoDinamico Campo { get; } = campo;

    public string Etiqueta => Campo.Unidad is { Length: > 0 } u ? $"{Campo.Etiqueta} ({u})" : Campo.Etiqueta;

    public bool EsBooleano => Campo.TipoDato == TiposDatoCampo.Booleano;

    public bool EsSeleccion => Campo.TipoDato == TiposDatoCampo.Seleccion;

    public bool EsTextoLibre => !EsBooleano && !EsSeleccion;

    public IReadOnlyList<string> Opciones => Campo.Opciones ?? [];

    public string Ayuda => Campo.TipoDato switch
    {
        TiposDatoCampo.Numero => "Número entero.",
        TiposDatoCampo.NumeroDecimal => "Número; usa coma o punto para los decimales.",
        TiposDatoCampo.Fecha => "dd/mm/aaaa",
        _ => string.Empty,
    };

    [ObservableProperty]
    private bool _editable = true;

    [ObservableProperty]
    private string _texto = string.Empty;

    /// <summary>Sí / No; null = sin responder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Si), nameof(No))]
    private bool? _valorBooleano;

    [ObservableProperty]
    private string? _opcion;

    [ObservableProperty]
    private string? _error;

    public bool Si
    {
        get => ValorBooleano == true;
        set
        {
            if (value)
            {
                ValorBooleano = true;
            }
        }
    }

    public bool No
    {
        get => ValorBooleano == false;
        set
        {
            if (value)
            {
                ValorBooleano = false;
            }
        }
    }

    partial void OnTextoChanged(string value) => Error = null;

    partial void OnValorBooleanoChanged(bool? value) => Error = null;

    partial void OnOpcionChanged(string? value) => Error = null;

    /// <summary>Valor con su tipo JSON (long, decimal, string, bool o fecha yyyy-MM-dd); null si está vacío.</summary>
    public bool Leer(out object? valor)
    {
        valor = null;
        Error = null;
        if (EsBooleano)
        {
            valor = ValorBooleano;
        }
        else if (EsSeleccion)
        {
            valor = string.IsNullOrWhiteSpace(Opcion) ? null : Opcion;
        }
        else if (Texto.Trim() is { Length: > 0 } texto)
        {
            switch (Campo.TipoDato)
            {
                case TiposDatoCampo.Numero:
                    if (long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entero))
                    {
                        valor = entero;
                    }
                    else
                    {
                        Error = "Escribe un número entero.";
                    }

                    break;
                case TiposDatoCampo.NumeroDecimal:
                    if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numero))
                    {
                        valor = numero;
                    }
                    else
                    {
                        Error = IntervencionViewModel.NumeroInvalido;
                    }

                    break;
                case TiposDatoCampo.Fecha:
                    if (FormularioEquipoViewModel.Fecha(texto) is { } fecha)
                    {
                        valor = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        Error = "Escribe la fecha como dd/mm/aaaa.";
                    }

                    break;
                default:
                    valor = texto;
                    break;
            }
        }

        if (Error is null && valor is null && Campo.Obligatorio)
        {
            Error = IntervencionViewModel.Obligatorio;
        }

        return Error is null;
    }

    /// <summary>Carga el valor guardado en la intervención.</summary>
    public void Mostrar(JsonElement valor)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.True or JsonValueKind.False:
                ValorBooleano = valor.GetBoolean();
                break;
            case JsonValueKind.Number:
                Texto = Campo.TipoDato == TiposDatoCampo.NumeroDecimal
                    ? valor.GetDecimal().ToString("0.############", TextosEquipo.Cultura)
                    : valor.GetRawText();
                break;
            case JsonValueKind.String when EsSeleccion:
                Opcion = Opciones.FirstOrDefault(o => string.Equals(o, valor.GetString(), StringComparison.CurrentCultureIgnoreCase)) ?? valor.GetString();
                break;
            case JsonValueKind.String when Campo.TipoDato == TiposDatoCampo.Fecha
                                           && DateOnly.TryParseExact(valor.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha):
                Texto = fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                break;
            case JsonValueKind.String:
                Texto = valor.GetString() ?? string.Empty;
                break;
        }
    }
}
