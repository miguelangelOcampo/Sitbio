using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-008 Importar inventario (HU-016; prototipo screens/Importacion.tsx): asistente de tres pasos.
/// 1) descargar la plantilla y cargar el archivo; 2) revisar el resultado por fila (vista previa de la API,
/// nada se guarda); 3) confirmar: se registran las filas válidas que caben en el plan (RN-009). Si quedan
/// filas con errores se muestra MSG-015 con «Descargar informe». Solo AC-01 y AC-02 llegan aquí; la API
/// vuelve a validar el permiso y cada fila (RNF-003).
/// </summary>
public partial class ImportacionViewModel(IServicioImportacion servicio, IDialogoArchivos dialogo) : ObservableObject, ISolicitaNavegacion
{
    public event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;

    public const string IdPantalla = "UI-008";
    public const int TamanoMaximoMb = 5;
    public const string FiltroArchivos = "Excel o CSV (*.xlsx;*.csv)|*.xlsx;*.csv";
    public const string Msg015 = "Se importaron {0} equipos. {1} filas tienen errores; descargue el informe para corregirlas.";
    private static readonly string[] Extensiones = [".xlsx", ".csv"];

    public static IReadOnlyList<string> Pasos { get; } = ["Cargar archivo", "Revisar filas", "Confirmar"];

    /// <summary>1 = cargar, 2 = revisar, 3 = confirmar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnCargar), nameof(EnRevisar), nameof(EnConfirmar), nameof(PasosEstado))]
    private int _paso = 1;

    public bool EnCargar => Paso == 1;

    public bool EnRevisar => Paso == 2;

    public bool EnConfirmar => Paso == 3;

    /// <summary>Indicador de pasos: el actual resaltado y los anteriores con ✓ (todos, al terminar).</summary>
    public IReadOnlyList<PasoAsistente> PasosEstado =>
        Pasos.Select((nombre, i) => new PasoAsistente(i + 1, nombre, Actual: !Terminado && Paso == i + 1, Hecho: Terminado || Paso > i + 1)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Libre))]
    private bool _ocupado;

    public bool Libre => !Ocupado;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    // ---------- Paso 1 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayArchivo), nameof(NombreArchivo), nameof(TamanoArchivo))]
    private string? _rutaArchivo;

    private long _bytesArchivo;

    public bool HayArchivo => RutaArchivo is not null;

    public string NombreArchivo => RutaArchivo is null ? "Ningún archivo seleccionado" : Path.GetFileName(RutaArchivo);

    public string TamanoArchivo => RutaArchivo is null ? string.Empty : FilaDocumento.Tamano(_bytesArchivo);

    [ObservableProperty]
    private string? _errorArchivo;

    // ---------- Paso 2 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(TotalFilas), nameof(FilasValidas), nameof(FilasConError), nameof(Importables), nameof(SinCupo), nameof(FilasOmitidas),
        nameof(HayErrores), nameof(SinErrores), nameof(TextoContinuar), nameof(PuedeContinuar), nameof(TextoSinCupo), nameof(FilasVisibles))]
    private ResultadoImportacion? _revision;

    public int TotalFilas => Revision?.TotalFilas ?? 0;

    public int FilasValidas => Revision?.FilasValidas ?? 0;

    public int FilasConError => TotalFilas - FilasValidas;

    public int Importables => Revision?.Importables ?? 0;

    /// <summary>Filas válidas que no caben en el plan (RN-009).</summary>
    public int SinCupo => FilasValidas - Importables;

    /// <summary>Con errores o sin cupo en el plan: no se importan.</summary>
    public int FilasOmitidas => TotalFilas - Importables;

    public bool HayErrores => Revision?.Errores.Count > 0;

    public bool SinErrores => Revision is not null && !HayErrores;

    public bool PuedeContinuar => Importables > 0;

    public string TextoContinuar => $"Continuar con {Importables} {(Importables == 1 ? "fila válida" : "filas válidas")}";

    public string TextoSinCupo => Revision is null || SinCupo == 0
        ? string.Empty
        : $"El plan solo admite {Revision.CupoDisponible} equipos más: {(SinCupo == 1 ? "1 fila válida no se importará" : $"{SinCupo} filas válidas no se importarán")}. Solicita la ampliación del plan o da de baja equipos fuera de uso.";

    /// <summary>Resultado por fila: las que no se importarán, con todos sus motivos (la API no devuelve las válidas).</summary>
    public IReadOnlyList<FilaRevision> FilasVisibles =>
        Revision is null ? [] : Revision.Errores.GroupBy(e => e.Fila).OrderBy(g => g.Key).Select(g => new FilaRevision(g.Key, g.First().Placa, g.ToList())).ToList();

    // ---------- Paso 3 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Terminado), nameof(PorConfirmar), nameof(TextoTerminado), nameof(Msg015Visible), nameof(TextoMsg015), nameof(PasosEstado))]
    private ResultadoImportacion? _resultado;

    public bool Terminado => Resultado is not null;

    public bool PorConfirmar => !Terminado;

    public string TextoTerminado => Resultado is null ? string.Empty : $"{Resultado.Importadas} equipos se agregaron al inventario en estado «Operativo».";

    public bool Msg015Visible => Resultado?.Errores.Count > 0;

    public string TextoMsg015 => Resultado is null
        ? string.Empty
        : string.Format(TextosEquipo.Cultura, Msg015, Resultado.Importadas, Resultado.Errores.Select(e => e.Fila).Distinct().Count());

    [RelayCommand]
    private Task DescargarPlantillaAsync(string? formato) => GuardarAsync(
        async () => await servicio.DescargarPlantillaAsync(formato ?? "xlsx", CancellationToken.None),
        $"plantilla-importacion-equipos.{formato ?? "xlsx"}",
        "Plantilla descargada",
        "No pudimos descargar la plantilla");

    [RelayCommand]
    private void ElegirArchivo()
    {
        var ruta = dialogo.ElegirArchivo("Importar inventario", FiltroArchivos);
        if (ruta is null)
        {
            return;
        }

        _bytesArchivo = dialogo.Tamano(ruta);
        RutaArchivo = ruta;
        ErrorArchivo = ValidarArchivo();
        Revision = null;
        Aviso = null;
    }

    /// <summary>Vista previa: la API valida cada fila sin guardar nada.</summary>
    [RelayCommand]
    private async Task RevisarAsync()
    {
        ErrorArchivo = ValidarArchivo();
        if (ErrorArchivo is not null || Ocupado)
        {
            return;
        }

        Ocupado = true;
        try
        {
            Revision = await servicio.ProcesarAsync(NombreArchivo, dialogo.Leer(RutaArchivo!), confirmar: false, CancellationToken.None);
            Aviso = null;
            Confirmacion = null;
            Paso = 2;
        }
        catch (ErrorApi error)
        {
            ErrorArchivo = error.Errores.TryGetValue("archivo", out var archivo) ? archivo : null;
            Aviso = ErrorArchivo is null ? AvisoDe(error, "No pudimos revisar el archivo") : null;
        }
        catch (IOException)
        {
            ErrorArchivo = "No se pudo leer el archivo. Verifica que exista y no esté abierto en otro programa.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Continuar()
    {
        if (PuedeContinuar)
        {
            Paso = 3;
        }
    }

    [RelayCommand]
    private void Atras()
    {
        if (Paso > 1 && !Terminado)
        {
            Paso--;
        }
    }

    /// <summary>Registra las filas válidas que caben en el plan; la API vuelve a validar todo el archivo.</summary>
    [RelayCommand]
    private async Task ConfirmarAsync()
    {
        if (!PuedeContinuar || Ocupado || Terminado)
        {
            return;
        }

        Ocupado = true;
        try
        {
            Resultado = await servicio.ProcesarAsync(NombreArchivo, dialogo.Leer(RutaArchivo!), confirmar: true, CancellationToken.None);
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, "No pudimos importar los equipos");
        }
        catch (IOException)
        {
            Aviso = new Aviso("No pudimos leer el archivo", "Verifica que exista y no esté abierto en otro programa.", null);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Informe de errores por fila en CSV (UTF-8, punto y coma), para corregir el archivo.</summary>
    [RelayCommand]
    private Task DescargarInformeAsync()
    {
        var errores = (Resultado ?? Revision)?.Errores;
        if (errores is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        return GuardarAsync(
            () => Task.FromResult(new ArchivoDescargado(Informe(errores), "informe-importacion.csv")),
            $"informe-importacion-{Path.GetFileNameWithoutExtension(NombreArchivo)}.csv",
            "Informe descargado",
            "No pudimos guardar el informe");
    }

    [RelayCommand]
    private void ImportarOtro()
    {
        RutaArchivo = null;
        _bytesArchivo = 0;
        ErrorArchivo = null;
        Revision = null;
        Resultado = null;
        Aviso = Confirmacion = null;
        Paso = 1;
    }

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    /// <summary>«Ver inventario» abre UI-005 con los equipos recién importados.</summary>
    [RelayCommand]
    private void VerInventario() => NavegacionSolicitada?.Invoke(this, new SolicitudNavegacion("UI-005"));

    public static byte[] Informe(IReadOnlyList<ErrorFilaImportacion> errores)
    {
        static string Celda(string? valor) =>
            valor is null ? string.Empty : valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : valor;

        var texto = new StringBuilder("Fila;Placa de inventario;Columna;Motivo\r\n");
        foreach (var e in errores.OrderBy(e => e.Fila))
        {
            texto.Append(e.Fila.ToString(TextosEquipo.Cultura)).Append(';').Append(Celda(e.Placa)).Append(';')
                .Append(Celda(e.Columna)).Append(';').Append(Celda(e.Mensaje)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())];
    }

    private async Task GuardarAsync(Func<Task<ArchivoDescargado>> obtener, string nombreSugerido, string tituloListo, string tituloError)
    {
        var destino = dialogo.ElegirDestino(nombreSugerido);
        if (destino is null)
        {
            return;
        }

        try
        {
            var archivo = await obtener();
            dialogo.Guardar(destino, archivo.Contenido);
            Confirmacion = new Aviso(tituloListo, $"{Path.GetFileName(destino)} quedó guardado en {Path.GetDirectoryName(destino)}.", null, EsError: false);
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, tituloError);
        }
        catch (IOException)
        {
            Aviso = new Aviso("No pudimos guardar el archivo", "Elige otra carpeta o cierra el archivo si está abierto en otro programa.", null);
        }
        catch (UnauthorizedAccessException)
        {
            Aviso = new Aviso("No pudimos guardar el archivo", "No tienes permiso para escribir en esa carpeta. Elige otra.", null);
        }
    }

    private string? ValidarArchivo()
    {
        if (RutaArchivo is null)
        {
            return "Selecciona el archivo con el inventario.";
        }

        if (!Extensiones.Contains(Path.GetExtension(RutaArchivo), StringComparer.OrdinalIgnoreCase))
        {
            return "Solo se aceptan archivos CSV o Excel (.xlsx).";
        }

        return _bytesArchivo > TamanoMaximoMb * 1024L * 1024L ? $"El archivo supera {TamanoMaximoMb} MB." : null;
    }

    private static Aviso AvisoDe(ErrorApi error, string titulo) => new(
        error.SinConexion ? "Sin conexión" : titulo,
        error.Message,
        error.SinConexion ? "MSG-011" : error.Codigo,
        PuedeReintentar: error.SinConexion);
}

/// <summary>Una fila del archivo con sus errores: «Fila 4 · PL-00123 · Placa de inventario: ya existe…».</summary>
public sealed record FilaRevision(int Fila, string? Placa, IReadOnlyList<ErrorFilaImportacion> Errores)
{
    public string PlacaTexto => string.IsNullOrWhiteSpace(Placa) ? "—" : Placa;

    public IReadOnlyList<string> Motivos => Errores.Select(e => $"{e.Columna}: {e.Mensaje}").ToList();
}

/// <summary>Un paso del asistente: «1 Cargar archivo», resaltado si es el actual y con ✓ si ya pasó.</summary>
public sealed record PasoAsistente(int Numero, string Nombre, bool Actual, bool Hecho)
{
    public string Marca => Hecho ? "✓" : Numero.ToString(TextosEquipo.Cultura);
}
